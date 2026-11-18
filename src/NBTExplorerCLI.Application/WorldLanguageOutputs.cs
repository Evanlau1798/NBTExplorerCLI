using System.Text;
using System.Text.Json;
using System.IO.Compression;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal static class WorldLanguageOutputs
{
    internal static void BindExisting(IWorldInventory inventory, WorldQuerySnapshot snapshot, IReadOnlyList<PatchOperation> operations,
        Dictionary<(ArtifactAddress Artifact, ChunkPosition? Chunk), DiscoveredSource> sources)
    {
        var languages = operations.OfType<KeyConversionPatch>().Select(item => item.Conversion.Dependency.LanguageArtifact)
            .Distinct().Where(artifact => !sources.ContainsKey((artifact, null))).ToArray();
        if (languages.Length == 0) return;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            var outer = language.ContainerEntry is null ? language : new ArtifactAddress(language.WorldId, language.RelativePath, language.Kind);
            if (!sources.ContainsKey((outer, null))) wanted.Add(language.RelativePath);
            var assets = language.RelativePath.LastIndexOf("/assets/", StringComparison.Ordinal);
            if (language.ContainerEntry is null && assets >= 0) wanted.Add(language.RelativePath[..assets] + "/pack.mcmeta");
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Discovery.Inventory.Entries)
            if (wanted.Contains(entry.RelativePath)) paths.Add(entry.RelativePath);
        if (paths.Count == 0) return;
        // Output dependencies are read independently; they never widen the gameplay snapshot or its slot selection.
        var discovery = WorldDiscovery.Discover(inventory, new(new([], [], [], paths), snapshot.Limits));
        if (discovery.DiagnosticCode is { } diagnostic)
        {
            if (diagnostic == "SourceLimitExceeded") throw new CodecException(CodecError.LimitExceeded, 0, diagnostic);
            throw new EditRejectedException(EditFailure.Precondition, "Language dependency discovery is incomplete: " + diagnostic);
        }
        foreach (var source in discovery.Sources)
            if (sources.TryAdd((source.Location.Artifact, source.Chunk), source) && sources.Count > 100_000)
                throw new CodecException(CodecError.LimitExceeded, 0, "Combined plan sources exceed the source budget");
    }
    internal static HashSet<ArtifactAddress> AddMissingLanguages(WorldQuerySnapshot snapshot, IReadOnlyList<PatchOperation> operations,
        Dictionary<(ArtifactAddress Artifact, ChunkPosition? Chunk), DiscoveredSource> sources)
    {
        var added = new HashSet<ArtifactAddress>(); long retained = 0;
        foreach (var operation in operations.OfType<KeyConversionPatch>())
        {
            var conversion = operation.Conversion; var language = conversion.Dependency.LanguageArtifact;
            if (!ResourceName(conversion.Dependency.Locale)) throw new EditRejectedException(EditFailure.Precondition, "Language locale must be a lowercase Minecraft resource filename");
            if (sources.ContainsKey((language, null))) continue;
            var outer = language.ContainerEntry is null ? language : conversion.Dependency.PackArtifact!;
            var knownArchive = language.ContainerEntry is not null && sources.TryGetValue((outer, null), out var archiveSource)
                && archiveSource.Outcome == SourceOutcome.Success && archiveSource.Capability.Read == SupportLevel.Supported && archiveSource.Pack?.Kind == PackSourceKind.ResourcePack;
            if (!knownArchive && snapshot.Discovery.Inventory.Entries.Any(entry => entry.RelativePath.Equals(outer.RelativePath, StringComparison.OrdinalIgnoreCase)))
                throw new EditRejectedException(EditFailure.Precondition, "Existing language output must be included as a supported exact source");
            if (snapshot.Discovery.Inventory.Entries.Any(entry => outer.RelativePath.StartsWith(entry.RelativePath + "/", StringComparison.OrdinalIgnoreCase)))
                throw new EditRejectedException(EditFailure.Precondition, "New output has an unusable parent path");
            var profile = MinecraftProfiles.ForId(conversion.Profile) ?? throw new NotSupportedException("Unknown generated language profile");
            PackMetadata pack; string packRoot; Hash256? metadataHash = null;
            string entryPath;
            if (language.ContainerEntry is { } entry)
            {
                if (!outer.RelativePath.EndsWith(".zip", StringComparison.Ordinal) || outer.RelativePath.StartsWith("datapacks/", StringComparison.Ordinal))
                    throw new EditRejectedException(EditFailure.Precondition, "Generated resource pack requires an explicit resource ZIP output");
                packRoot = outer.RelativePath; entryPath = entry;
                if (sources.TryGetValue((outer, null), out var existing)) pack = existing.Pack!;
                else
                {
                    pack = PackMetadata.Read(Metadata(profile), PackSourceKind.ResourcePack, snapshot.Limits);
                    sources.Add((outer, null), new(new(outer, WorldSourceRole.Archive, PackRoot: packRoot, PackKind: PackSourceKind.ResourcePack), null, SourceOutcome.Success, null, null, new(Read: SupportLevel.Supported), pack));
                }
            }
            else
            {
                var owner = sources.Values.Concat(snapshot.Discovery.Sources).Where(source => source.Pack?.Kind == PackSourceKind.ResourcePack && source.Location.PackRoot is { } root
                    && language.RelativePath.StartsWith(root + "/assets/", StringComparison.Ordinal) && source.PackMetadataHash is not null)
                    .OrderByDescending(source => source.Location.PackRoot!.Length).FirstOrDefault()
                    ?? throw new EditRejectedException(EditFailure.Precondition, "Missing language output requires a known exact resource pack");
                packRoot = owner.Location.PackRoot!; pack = owner.Pack!; metadataHash = owner.PackMetadataHash;
                entryPath = language.RelativePath[(packRoot.Length + 1)..];
            }
            var parts = entryPath.Split('/');
            if (parts.Length != 4 || parts[0] != "assets" || parts[2] != "lang" || !ResourceName(parts[1])
                || parts[3] != conversion.Dependency.Locale + ".json")
                throw new EditRejectedException(EditFailure.Precondition, "New language output must name an exact namespace and locale");
            var binding = new SourceAddress(language, TypedPath.Root, "language.value");
            retained = checked(retained + TextExtractionBudget.RecordBytes(binding, 0));
            if (retained > snapshot.Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Generated language declarations exceed retention budget");
            sources.Add((language, null), new(new(language, WorldSourceRole.Language, PackRoot: packRoot, PackKind: PackSourceKind.ResourcePack), null,
                SourceOutcome.Success, null, null, new(Read: SupportLevel.Supported), pack, metadataHash));
            added.Add(language);
        }
        return added;
    }
    private static bool ResourceName(string value) => value.Length != 0 && value.All(unit => char.IsAsciiLetterLower(unit) || char.IsAsciiDigit(unit) || unit is '_' or '-' or '.');
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, IReadOnlyList<PatchOperation> operations, DecodeLimits limits)
    {
        if (operations.Any(operation => operation is not KeyConversionPatch)) throw new EditRejectedException(EditFailure.Precondition, "New outputs require declared language dependencies");
        if (artifact.Kind == ArtifactKind.Json) return WorldLanguageEdits.Create(artifact, "{}"u8.ToArray(), operations, limits);
        var keys = operations.Cast<KeyConversionPatch>().ToArray();
        var profile = MinecraftProfiles.ForId(keys[0].Conversion.Profile) ?? throw new NotSupportedException("Unknown generated pack profile");
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["pack.mcmeta"] = Metadata(profile) }; long retained = entries["pack.mcmeta"].Length;
        foreach (var group in keys.GroupBy(operation => operation.Conversion.Dependency.LanguageArtifact))
        {
            var language = WorldLanguageEdits.Create(group.Key, "{}"u8.ToArray(), group.Cast<PatchOperation>().ToArray(), limits);
            retained = checked(retained + language.Bytes.Length + Encoding.UTF8.GetByteCount(group.Key.ContainerEntry!) * 2L + 128);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Generated pack entries exceed retention budget");
            using (var stream = new MemoryStream(language.Bytes, writable: false)) language.Verify(stream);
            entries.Add(group.Key.ContainerEntry!, language.Bytes);
        }
        using var output = new LimitedZipOutput(limits.MaxDecodedBytes);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var pair in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(pair.Key, CompressionLevel.NoCompression); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open(); stream.Write(pair.Value);
            }
        var bytes = output.ToArray(); var hashes = entries.ToDictionary(pair => pair.Key, pair => Hash256.Compute(pair.Value), StringComparer.Ordinal);
        void Verify(Stream stream)
        {
            using var zip = ZipArchiveValidation.OpenRead(WorldArtifactEdits.Read(stream), limits);
            if (zip.Entries.Count != hashes.Count) throw new InvalidDataException("Generated pack entry set differs");
            foreach (var entry in zip.Entries)
            {
                using var value = entry.Open();
                if (!hashes.TryGetValue(entry.FullName, out var expected) || new Hash256(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value))) != expected)
                    throw new InvalidDataException("Generated pack entry differs from its declared image");
            }
        }
        using (var stream = new MemoryStream(bytes, writable: false)) Verify(stream);
        return new(bytes, Verify);
    }
    private static byte[] Metadata(CapabilityProfile profile)
    {
        var format = MinecraftProfiles.RequiredResourcePackFormat(profile);
        return format.Major < 65 ? JsonSerializer.SerializeToUtf8Bytes(new { pack = new { pack_format = format.Major, description = "NBTExplorerCLI translations" } })
            : JsonSerializer.SerializeToUtf8Bytes(new { pack = new { min_format = new[] { format.Major, format.Minor }, max_format = new[] { format.Major, format.Minor }, description = "NBTExplorerCLI translations" } });
    }
    private sealed class LimitedZipOutput(long limit) : MemoryStream
    {
        private void Check(int count) { if (count > limit - Position) throw new CodecException(CodecError.LimitExceeded, Position, "Generated ZIP exceeds output budget"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
}
