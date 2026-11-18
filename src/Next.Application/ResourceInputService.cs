using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Next.Application;

public sealed class ResourceInputService(IWorldBackupStore objects)
{
    private readonly IWorldBackupStore objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private const int MaximumBytes = 67_108_864;
    private static readonly DecodeLimits Limits = DecodeLimits.Default with { MaxDecodedBytes = MaximumBytes, MaxContainerEntries = 100_000 };
    public ResourceGeneration Capture(ResourceGeneration request, IWorldInventory source, ArtifactAddress? archive = null)
    {
        RequirePack(request); ArgumentNullException.ThrowIfNull(source);
        if (request.Merge is not null) throw new ArgumentException("Capture requires an unmerged resource intent", nameof(request));
        var access = source.AccessBinding ?? throw new ArgumentException("Resource input requires a bound physical source");
        var inputs = new List<ArtifactPrecondition>(); Dictionary<string, byte[]> files; WorldInventorySnapshot? catalog = null;
        if (archive is not null)
        {
            if (archive.WorldId != source.WorldId || archive.Kind != ArtifactKind.Archive || archive.ContainerEntry is not null) throw new ArgumentException("Merge ZIP must be an exact physical source artifact");
            var bytes = source.Read(archive); if (bytes.Length > MaximumBytes) throw Limit();
            inputs.Add(new(archive, ArtifactImage.Present(Hash256.Compute(bytes)))); files = ZipFiles(bytes);
        }
        else
        {
            catalog = source.Enumerate(); RequireCatalog(catalog); files = new(StringComparer.Ordinal); long retained = 0;
            foreach (var entry in catalog.Entries)
            {
                var artifact = new ArtifactAddress(source.WorldId, entry.RelativePath, entry.RelativePath.EndsWith(".json", StringComparison.Ordinal) || entry.RelativePath == "pack.mcmeta" ? ArtifactKind.Json : ArtifactKind.Text);
                var bytes = source.Read(artifact); retained += bytes.Length; if (retained > MaximumBytes) throw Limit();
                inputs.Add(new(artifact, ArtifactImage.Present(Hash256.Compute(bytes)))); files.Add(entry.RelativePath, bytes);
            }
        }
        ValidateFiles(files, request.PackFormat!);
        var manifest = new BackupManifest(source.WorldId, inputs.Select(input => new BackupArtifact(input.Artifact, input.Image)));
        var index = Encoding.UTF8.GetBytes(BackupManifestJson.Write(manifest)); var hash = Hash256.Compute(index);
        var generation = new ResourceGeneration(request.WorldId, request.Format, request.Destination, request.Languages, request.PackFormat,
            new(hash, Entries(files)));
        var backup = new WorldBackupService(source, objects).Create(inputs);
        if (backup.ManifestHash != hash) throw new InvalidDataException("Resource input backup differs from its complete source manifest");
        if (catalog is not null)
        {
            var current = source.Enumerate(); RequireCatalog(current);
            if (!catalog.Entries.Select(entry => entry.RelativePath).Order(StringComparer.Ordinal).SequenceEqual(current.Entries.Select(entry => entry.RelativePath).Order(StringComparer.Ordinal)))
                throw new ArtifactConflictException("Resource input file set changed during capture");
        }
        if (source.AccessBinding != access) throw new ArtifactConflictException("Resource input root changed during capture");
        objects.SaveBlob(hash, index);
        _ = Load(generation); return generation;
    }
    public ResourceInputSnapshot Load(ResourceGeneration request)
    {
        RequirePack(request); var merge = request.Merge ?? throw new ArgumentException("Resource intent has no frozen merge input", nameof(request));
        var index = Blob(merge.BlobHash); var manifest = BackupManifestJson.Read(new UTF8Encoding(false, true).GetString(index));
        if (manifest.Artifacts.IsEmpty || manifest.Artifacts.Any(entry => entry.Image.ExpectedAbsent)) throw new InvalidDataException("Resource input cannot declare absent source files");
        Dictionary<string, byte[]> files;
        if (manifest.Artifacts is [{ Artifact.Kind: ArtifactKind.Archive } one]) files = ZipFiles(Blob(one.Image.Hash!));
        else
        {
            if (manifest.Artifacts.Any(entry => entry.Artifact.Kind is not (ArtifactKind.Json or ArtifactKind.Text))) throw new InvalidDataException("Unknown resource input manifest shape");
            files = new(StringComparer.Ordinal); long retained = 0;
            foreach (var entry in manifest.Artifacts)
            {
                var bytes = Blob(entry.Image.Hash!); retained += bytes.Length; if (retained > MaximumBytes) throw Limit();
                files.Add(entry.Artifact.RelativePath, bytes);
            }
        }
        var actual = Entries(files);
        if (!actual.SequenceEqual(merge.Entries)) throw new InvalidDataException("Resource input files differ from the declared path/hash/length manifest");
        ValidateFiles(files, request.PackFormat!);
        return new(files, actual);
    }
    private byte[] Blob(Hash256 hash)
    {
        var bytes = objects.ReadBlob(hash) ?? throw new FileNotFoundException("Required resource input object is missing");
        if (bytes.Length > MaximumBytes || Hash256.Compute(bytes) != hash) throw new InvalidDataException("Resource input object differs from its immutable hash");
        return bytes;
    }
    private static ImmutableArray<ResourceMergeEntry> Entries(Dictionary<string, byte[]> files)
        => files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new ResourceMergeEntry(file.Key, Hash256.Compute(file.Value), file.Value.Length)).ToImmutableArray();
    internal static Dictionary<string, byte[]> ZipFiles(byte[] bytes)
    {
        using var archive = ZipArchiveValidation.OpenRead(bytes, Limits); var files = new Dictionary<string, byte[]>(StringComparer.Ordinal); long retained = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                if (!IsResourceDirectory(entry.FullName[..^1])) throw new NotSupportedException("Resource ZIP contains a non-resource directory");
                continue;
            }
            retained = checked(retained + entry.Length); if (retained > MaximumBytes) throw Limit();
            var contents = new byte[checked((int)entry.Length)]; using var stream = entry.Open(); stream.ReadExactly(contents); files.Add(entry.FullName, contents);
        }
        return files;
    }
    internal static void RequirePack(ResourceGeneration request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Format == ResourceOutputFormat.LanguageFiles || request.PackFormat is null) throw new ArgumentException("Merge input requires a declared pack output");
        var format = request.PackFormat;
        if (MinecraftProfiles.ForPackMetadata(new(PackSourceKind.ResourcePack, format.Major < 65 ? format.Major : null, format, format)) is null)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown resource generation pack format");
    }
    internal static void ValidateFiles(Dictionary<string, byte[]> files, PackFormatVersion required, ResourceGeneration? output = null)
    {
        if (!files.TryGetValue("pack.mcmeta", out var metadata)) throw new InvalidDataException("Resource input is missing pack metadata");
        PackMetadata pack;
        using (ResourceRendering.ObserveMember(output, "pack.mcmeta", "resource.validate")) pack = PackMetadata.Read(metadata, PackSourceKind.ResourcePack, Limits);
        if (MinecraftProfiles.ForPackMetadata(pack) is null || !pack.DeclaresSupport(required)) throw new CodecException(CodecError.UnsupportedProfile, 0, "Resource input format does not support the requested output");
        foreach (var file in files)
        {
            if (!file.Key.Contains('/') && (file.Key.Equals("level.dat", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("level.dat_old", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("session.lock", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("data", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("datapacks", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("region", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("entities", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("poi", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("players", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("playerdata", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("dimensions", StringComparison.OrdinalIgnoreCase)
                || file.Key.Equals("DIM1", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("DIM-1", StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("Resource pack cannot carry world metadata or gameplay roots");
            var path = file.Key.AsSpan();
            if (!path.StartsWith("assets/", StringComparison.Ordinal)) continue;
            path = path[7..]; var slash = path.IndexOf('/');
            if (slash <= 0) continue;
            path = path[(slash + 1)..];
            if (!path.StartsWith("lang/", StringComparison.Ordinal)) continue;
            path = path[5..];
            if (path.Contains('/') || !path.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var observation = ResourceRendering.ObserveMember(output, file.Key, "resource.validate");
            using var json = BoundedJson.ReadDecoded(file.Value, Limits);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Resource language must be a string object");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in json.RootElement.EnumerateObject())
            { if (!keys.Add(NativeJsonStrings.PropertyName(property))) throw new JsonException("Duplicate resource language key"); _ = NativeJsonStrings.Read(property.Value); }
        }
    }
    private static void RequireCatalog(WorldInventorySnapshot catalog)
    {
        if (!catalog.Complete || catalog.Entries.Any(entry => entry.IsDirectory)) throw new IOException("Resource input catalog is incomplete or contains unsupported entries");
        if (catalog.DirectoryPaths.Any(path => !IsResourceDirectory(path))) throw new NotSupportedException("Resource input contains a non-resource directory");
        RequireByteBudget(catalog.Entries.Select(entry => entry.Bytes));
    }
    internal static void RequireByteBudget(IEnumerable<long> lengths)
    {
        long retained = 0;
        foreach (var bytes in lengths) { if (bytes < 0 || bytes > MaximumBytes - retained) throw Limit(); retained += bytes; }
    }
    internal static bool IsResourceDirectory(string path) => path == "assets" || path.StartsWith("assets/", StringComparison.Ordinal);
    private static CodecException Limit() => new(CodecError.LimitExceeded, 0, "Resource input exceeds its aggregate byte budget");
}

public sealed class ResourceInputSnapshot
{
    private readonly IReadOnlyDictionary<string, byte[]> files;
    public ImmutableArray<ResourceMergeEntry> Entries { get; }
    internal ResourceInputSnapshot(IReadOnlyDictionary<string, byte[]> files, ImmutableArray<ResourceMergeEntry> entries) => (this.files, Entries) = (files, entries);
    public byte[] Read(string path) => files.TryGetValue(path, out var bytes) ? bytes.ToArray() : throw new KeyNotFoundException("Resource input entry is absent");
}
