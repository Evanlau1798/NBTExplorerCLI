using System.Globalization;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal sealed class QuerySource : IDisposable
{
    internal string File { get; }
    internal ArtifactAddress Artifact { get; private set; }
    internal Hash256 RawHash { get; }
    internal Hash256? EntryHash { get; private set; }
    internal QueryNode? Root { get; private set; }
    internal string RootName { get; private set; } = "";
    internal RegionFile? Region { get; }
    internal ChunkPosition? Chunk { get; }
    internal string? Dimension { get; }
    private JsonDocument? json;
    internal static WorldInventory Inventory(string root)
    {
        var probe = new WorldInventory(root, new("473a419f-48ab-43d4-bc1a-d742415d27ae"));
        var id = new Guid(Convert.FromHexString(probe.AccessBinding.Hex).AsSpan(0, 16));
        return new(root, id);
    }
    internal QuerySource(CliArguments request)
    {
        var input = Path.GetFullPath(request.Input!); var world = Directory.Exists(input);
        var root = world ? input : Path.GetDirectoryName(input)!;
        var relative = world ? request.Get("path") ?? "" : Path.GetFileName(input);
        if (relative.Length == 0) throw new CliFailure("INVALID_ARGUMENTS", "World lookup needs a relative artifact in --path");
        if (!world && request.Flag("path")) throw new CliFailure("INVALID_ARGUMENTS", "Use --pointer or --segments to select a node in a standalone file");
        var inventory = Inventory(root); var extension = Path.GetExtension(relative).ToLowerInvariant();
        var kind = extension switch { ".json" => ArtifactKind.Json, ".zip" => ArtifactKind.Archive, ".mca" or ".mcr" => Path.GetFileName(root) == "entities" || relative.StartsWith("entities/", StringComparison.Ordinal) || relative.Contains("/entities/", StringComparison.Ordinal) ? ArtifactKind.Entities : ArtifactKind.Region, _ => Path.GetFileName(relative) == "pack.mcmeta" ? ArtifactKind.Json : ArtifactKind.Nbt };
        Artifact = new(inventory.WorldId, relative, kind); File = Path.GetFullPath(Path.Combine(root, relative));
        var bytes = inventory.Read(Artifact); RawHash = Hash256.Compute(bytes);
        if (kind == ArtifactKind.Archive)
        {
            var name = request.Get("entry") ?? throw new CliFailure("INVALID_ARGUMENTS", "Archive lookup needs an exact --entry");
            ArtifactAddress.ValidatePath(name, true);
            using var archive = ZipArchiveValidation.OpenRead(bytes); var entry = archive.GetEntry(name) ?? throw new CliFailure("NOT_FOUND", "Archive entry is absent");
            if (entry.Length > DecodeLimits.Default.MaxDecodedBytes) throw new CliFailure("RESOURCE_LIMIT", "Archive entry exceeds byte budget");
            var payload = new byte[checked((int)entry.Length)]; using var stream = entry.Open(); stream.ReadExactly(payload); bytes = payload;
            Artifact = new(inventory.WorldId, relative, kind, name); EntryHash = Hash256.Compute(bytes); extension = Path.GetExtension(name).ToLowerInvariant();
            if (Path.GetFileName(name) == "pack.mcmeta") extension = ".json";
        }
        else if (request.Flag("entry")) throw new CliFailure("INVALID_ARGUMENTS", "--entry requires an archive");
        if (kind is ArtifactKind.Region or ArtifactKind.Entities)
        {
            if (request.Flag("compression")) throw new CliFailure("INVALID_ARGUMENTS", "Region compression is selected by its chunk header");
            Region = RegionFile.Read(bytes);
            if (request.Get("chunk") is not { } coordinates)
            {
                if (request.Command != "list") throw new CliFailure("CHUNK_REQUIRED", "Region lookup needs --chunk x,z and --dimension");
                if (request.Flag("dimension")) throw new CliFailure("INVALID_ARGUMENTS", "Select a chunk before binding a node dimension");
                if (request.Flag("pointer") || request.Flag("segments")) throw new CliFailure("CHUNK_REQUIRED");
                return;
            }
            var values = coordinates.Split(',');
            if (values.Length != 2 || !int.TryParse(values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) || !int.TryParse(values[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)) throw new CliFailure("INVALID_ARGUMENTS", "Chunk coordinates are x,z");
            Chunk = new(x, z); var region = RegionPosition();
            if (Chunk.RegionX != region.X || Chunk.RegionZ != region.Z) throw new CliFailure("INVALID_ARGUMENTS", "Chunk is outside this physical region");
            Dimension = request.Get("dimension");
            var classic = false;
            if (world)
            {
                var discovery = WorldDiscovery.Discover(inventory, new(new SnapshotScope([], [], [], [relative])));
                var source = discovery.Sources.SingleOrDefault(source => source.Location.Artifact == Artifact && source.Chunk == Chunk) ?? throw new CliFailure("NOT_FOUND");
                if (Dimension is not null && Dimension != source.Location.Dimension) throw new CliFailure("CONFLICT", "Dimension differs from the declared layout");
                Dimension = source.Location.Dimension;
                classic = discovery.Metadata?.Profile == MinecraftProfiles.ClassicAnvil && source.Location.Role == WorldSourceRole.Terrain;
            }
            if (Dimension is null) throw new CliFailure("INVALID_ARGUMENTS", "Standalone region lookup needs --dimension");
            var index = Chunk.LocalZ * 32 + Chunk.LocalX;
            if (!Region.Chunks[index].Present) throw new CliFailure("NOT_FOUND", "Region chunk is absent");
            if (Region.Chunks[index].Capability.Read != SupportLevel.Supported) throw new CliFailure("UNSUPPORTED_SOURCE", "Unsupported chunk compression");
            var document = (classic ? Region.ReadClassicAnvilChunk(index) : Region.ReadChunk(index)).Document;
            RootName = document.Name; Root = new(document.Root); return;
        }
        if (request.Flag("chunk") || request.Flag("dimension")) throw new CliFailure("INVALID_ARGUMENTS", "Chunk/dimension options require a region");
        if (kind == ArtifactKind.Json || extension == ".json")
        {
            if (request.Flag("compression")) throw new CliFailure("INVALID_ARGUMENTS", "Native JSON has no NBT compression override");
            json = BoundedJson.ReadDecoded(bytes); Root = new(json.RootElement);
        }
        else
        {
            var codec = request.Get("compression") switch { "raw" => NbtCompression.Raw, "gzip" => NbtCompression.GZip, "zlib" => NbtCompression.Zlib,
                _ => bytes.Length != 0 && bytes[0] is >= 1 and <= 12 ? NbtCompression.Raw : bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b ? NbtCompression.GZip : NbtCompression.Zlib };
            var document = CompressedNbt.ReadDocument(bytes, codec).Document; RootName = document.Name; Root = new(document.Root);
        }
    }
    internal (int X, int Z) RegionPosition()
    {
        var pieces = Path.GetFileName(Artifact.RelativePath).Split('.');
        if (pieces.Length != 4 || pieces[0] != "r" || !int.TryParse(pieces[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(pieces[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)) throw new CliFailure("INVALID_ARGUMENTS", "Expected an exact r.x.z region filename");
        if (x < int.MinValue / 32 || x > int.MaxValue / 32 || z < int.MinValue / 32 || z > int.MaxValue / 32) throw new CliFailure("INVALID_ARGUMENTS", "Region coordinates exceed the chunk address domain");
        return (x, z);
    }
    internal SourceAddress Address(TypedPath path) => new(Artifact, path, "query.node", Dimension, Chunk);
    internal object Description(TypedPath? path) => new { file = File, rawHash = RawHash, entryHash = EntryHash, artifact = Artifact, address = path is null ? null : Address(path) };
    public void Dispose() => json?.Dispose();
}
