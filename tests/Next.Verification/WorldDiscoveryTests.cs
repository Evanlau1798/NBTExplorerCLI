using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class WorldDiscoveryTests
{
    internal static void Put(string root, string path, byte[] bytes)
    {
        var target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
    }
    internal static byte[] Zip(params (string Path, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (path, text) in entries)
            { using var output = zip.CreateEntry(path).Open(); output.Write(Encoding.UTF8.GetBytes(text)); }
        return stream.ToArray();
    }
    internal const string LegacyPack = "{\"pack\":{\"pack_format\":48,\"description\":\"Data\"}}";
    internal const string ResourcePack = "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}";
    internal static byte[] Region()
    {
        var payload = CompressedNbt.WriteDocument(new("", new NbtCompound([])), NbtCompression.Zlib);
        var bytes = new byte[12288]; BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x201);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8192), (uint)payload.Length + 1); bytes[8196] = 2; payload.CopyTo(bytes, 8197);
        return bytes;
    }
    internal static void Level(string root, bool modern)
    {
        Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(modern ? 4774 : 3953, modern ? null : WorldMetadataTests.Dimensions), NbtCompression.GZip));
        if (modern) Put(root, "data/minecraft/world_gen_settings.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Separate(4774, WorldMetadataTests.Dimensions), NbtCompression.GZip));
    }
    public static void LiteralCatalogAndScope(string fixtureRoot)
    {
        foreach (var modern in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild();
            try
            {
                Level(root, modern);
                var overworld = modern ? "dimensions/minecraft/overworld/" : "";
                var nether = modern ? "dimensions/minecraft/the_nether/" : "DIM-1/";
                Put(root, overworld + "region/r.-1.0.mca", Region()); Put(root, nether + "entities/r.-1.0.mca", Region());
                Put(root, "dimensions/models/tags/worldgen/region/r.2.3.mca", Region());
                Put(root, "datapacks/folder/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
                Put(root, "datapacks/folder/data/models/function/start.mcfunction", Encoding.UTF8.GetBytes("say Hello"));
                Put(root, "packs/custom-name/pack.mcmeta", Encoding.UTF8.GetBytes(ResourcePack));
                Put(root, "packs/custom-name/assets/models/lang/en_us.json", Encoding.UTF8.GetBytes("{\"key\":\"Hi\"}"));
                Put(root, "not-resources.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/models/lang/en_us.json", "{\"key\":\"Hi\"}")));
                Put(root, "datapacks/another-name.zip", Zip(("pack.mcmeta", LegacyPack), ("data/models/function/start.mcfunction", "say Bye")));
                var inventory = new WorldInventory(root, World); var result = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
                using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "discovery", "source-manifest.json")));
                var actual = result.Sources.Select(source => string.Join('|', source.Location.Artifact.RelativePath, source.Location.Artifact.ContainerEntry ?? "",
                    source.Chunk is null ? "" : $"{source.Chunk.X},{source.Chunk.Z}", source.Location.Role.ToString(), source.Location.Dimension ?? "")).Order(StringComparer.Ordinal);
                Require(actual.SequenceEqual(expected.RootElement.GetProperty(modern ? "modern" : "legacy").EnumerateArray().Select(row => row.GetString()!).Order(StringComparer.Ordinal)),
                    "complete address/entry/chunk/role/dimension set equals fixed independent manifest, not just a count");
                Require(result.Sources.All(source => source.Capability.Read == SupportLevel.Supported && source.Capability.Write == SupportLevel.Unknown), "discovery read capability does not imply writable sources");
                Require(result.Complete && result.Metadata?.DataVersion == (modern ? 4774 : 3953), "known world/registry produce complete literal source catalog");
                var chunks = result.Sources.Where(source => source.Chunk is not null).ToArray();
                Require(chunks.Length == 3 && chunks.Any(source => source.Location.Dimension == "minecraft:overworld" && source.Chunk == new ChunkPosition(-32, 0))
                    && chunks.Any(source => source.Location.Dimension == "minecraft:the_nether" && source.Chunk == new ChunkPosition(-32, 0))
                    && chunks.Any(source => source.Location.Dimension == "models:tags/worldgen" && source.Chunk == new ChunkPosition(64, 96)), "literal region coordinates and registry ownership survive legacy/modern layouts");
                foreach (var path in new[] { "not-resources.zip", "datapacks/another-name.zip" })
                    Require(result.Sources.Count(source => source.Location.Artifact.RelativePath == path && source.Location.Artifact.ContainerEntry is not null) == 2, "each ZIP has its own exact entries, regardless of basename");
                Require(result.Sources.Any(source => source.Location.Role == WorldSourceRole.Language && source.Location.PackRoot == "packs/custom-name")
                    && result.Sources.Any(source => source.Location.Role == WorldSourceRole.Function && source.Location.PackRoot == "datapacks/folder"), "directory packs and namespace models remain visible");
                var scope = new SnapshotScope(["minecraft:the_nether"], ["r.-1.0.mca"], ["sign.front.line"], []);
                var filtered = WorldDiscovery.Discover(inventory, new(scope));
                Require(filtered.Scope == scope && filtered.Sources.Where(source => source.Chunk is not null).Single().Location.Dimension == "minecraft:the_nether"
                    && filtered.Sources.Any(source => source.Location.Artifact.ContainerEntry is not null), "one request reaches actual adapters without hiding global pack sources");
                var limited = WorldDiscovery.Discover(inventory, new(SnapshotScope.All, MaximumSources: 3));
                Require(!limited.Complete && limited.Sources.Length <= 3 && limited.DiagnosticCode == "SourceLimitExceeded", "expanded entry/chunk catalog has a hard total source budget");
            }
            finally { Directory.Delete(root, true); }
        }
    }
    public static void BrokenSourcesAndUnknownProfile()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false);
            Put(root, "region/r.0.0.mca", []);
            Put(root, "datapacks/duplicate.zip", Zip(("pack.mcmeta", LegacyPack), ("data/a/function/a.mcfunction", "say A"), ("data/a/function/a.mcfunction", "say B")));
            Put(root, "bad.zip", [1, 2, 3]);
            Put(root, "good.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/models/lang/en_us.json", "{}")));
            var inventory = new WorldInventory(root, World); var result = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(!result.Complete && new[] { "region/r.0.0.mca", "datapacks/duplicate.zip", "bad.zip" }.All(path => result.Sources.Any(source => source.Location.Artifact.RelativePath == path && source.Outcome == SourceOutcome.Failed))
                && result.Sources.Any(source => source.Location.Artifact.RelativePath == "good.zip" && source.Location.Artifact.ContainerEntry is not null && source.Outcome == SourceOutcome.Success), "bad region/ZIP/duplicate retain failures alongside good entry addresses");
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(9999, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            var unknown = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(!unknown.Complete && unknown.Metadata is null && unknown.DiagnosticCode == "UnsupportedProfile"
                && unknown.Sources.Any(source => source.Location.Artifact.RelativePath == "level.dat" && source.Outcome == SourceOutcome.Unsupported), "unknown profile cannot become zero-item complete success");
        }
        finally { Directory.Delete(root, true); }
    }
}
