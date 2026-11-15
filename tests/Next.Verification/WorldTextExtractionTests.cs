using System.Buffers.Binary;
using System.Text;
using System.IO.Compression;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.NbtItemTextTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldTextExtractionTests
{
    private static byte[] RegionWith(NbtDocument document)
    {
        var payload = CompressedNbt.WriteDocument(document, NbtCompression.Zlib);
        var sectors = (payload.Length + 5 + 4095) / 4096;
        var bytes = new byte[8192 + sectors * 4096]; BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(0x200 + sectors));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8192), (uint)payload.Length + 1); bytes[8196] = 2; payload.CopyTo(bytes, 8197); return bytes;
    }
    private static byte[] Player(string text)
    {
        var item = C(E("id", S("minecraft:paper")), E("components", C(E("minecraft:custom_name", Text(text, false)))));
        return CompressedNbt.WriteDocument(new("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item)))), NbtCompression.GZip);
    }
    public static void RealMixedSourcesAndCorruption()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false);
            var sign = C(E("id", S("minecraft:sign")), E("front_text", C(E("messages", L(Text("Same", false), Text("", false), Text("", false), Text("", false))))));
            Put(root, "region/r.-1.0.mca", RegionWith(new("", C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(sign))))));
            Put(root, "playerdata/user.dat", Player("Same")); Put(root, "data/broken.dat", [1, 2, 3]);
            Put(root, "datapacks/p.zip", Zip(("pack.mcmeta", LegacyPack), ("data/models/advancement/a.json", "{\"display\":{\"title\":{\"text\":\"Same\"}}}")));
            Put(root, "arbitrary-A.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/models/lang/en_us.json", "{\"item.custom.name\":\"Same\"}")));
            Put(root, "packs/B/pack.mcmeta", Encoding.UTF8.GetBytes(ResourcePack)); Put(root, "packs/B/assets/models/lang/en_us.json", Encoding.UTF8.GetBytes("{\"block.custom.name\":\"Same\"}"));
            var inventory = new WorldInventory(root, World); var before = inventory.Enumerate().Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))));
            var scope = new SnapshotScope([], [], ["sign.front.line", "item.name", "language.value", "advancement.title"], []);
            var scan = new WorldTextExtraction(inventory, new(scope)); var rows = scan.ReadSources().ToArray();
            var texts = rows.SelectMany(row => row.Text?.Occurrences ?? []).ToArray();
            Require(scan.Discovery.Complete && rows.Any(row => row.Source.Location.Artifact.RelativePath == "data/broken.dat" && row.Outcome == SourceOutcome.Failed), "bad payload is explicit after successful discovery, alongside good source results");
            var actual = texts.Select(text => string.Join('|', text.Address.Artifact.RelativePath, text.Address.Artifact.ContainerEntry ?? "", Row(text), text.Address.Dimension ?? "", text.Address.Chunk is null ? "" : $"{text.Address.Chunk.X},{text.Address.Chunk.Z}")).Order(StringComparer.Ordinal);
            var expected = new List<string> {
                "playerdata/user.dat||/Inventory/0/components/minecraft:custom_name|/text|item.name|Same||",
                "datapacks/p.zip|data/models/advancement/a.json|/display/title/text|<physical>|advancement.title|Same||",
                "arbitrary-A.zip|assets/models/lang/en_us.json|/item.custom.name|<physical>|language.value|Same||",
                "packs/B/assets/models/lang/en_us.json||/block.custom.name|<physical>|language.value|Same||" };
            for (var index = 0; index < 4; index++) expected.Add($"region/r.-1.0.mca||/block_entities/0/front_text/messages/{index}|/text|sign.front.line|{(index == 0 ? "Same" : "")}|minecraft:overworld|-32,0");
            Require(actual.SequenceEqual(expected.Order(StringComparer.Ordinal)) && texts.Select(text => text.ToSlot("en_us", "zh_tw").Id).Distinct().Count() == expected.Count,
                "real DAT/region/ZIP/directory adapters produce exact full address-role-text set with distinct identities");
            Require(before.Count == inventory.Enumerate().Entries.Length && before.All(pair => Hash256.Compute(inventory.Read(new(World, pair.Key, ArtifactKind.Text))) == pair.Value), "discovery/decode/extraction leaves whole input file set and raw bytes unchanged");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void StaleArtifactCannotUseOldSnapshot()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "playerdata/user.dat", Player("Before"));
            var scan = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All));
            Put(root, "playerdata/user.dat", Player("After"));
            var rows = scan.ReadSources().ToArray();
            Require(rows.Any(row => row.Source.Location.Artifact.RelativePath == "playerdata/user.dat" && row.Outcome == SourceOutcome.Failed && row.DiagnosticCode == "SourceChangedDuringExtraction")
                && rows.SelectMany(row => row.Text?.Occurrences ?? []).All(text => text.Text != "After"), "source raw hash is checked before decoding; changed input is not reinterpreted using stale discovery metadata");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class MetadataRace(IWorldInventory actual, string root) : IWorldInventory
    {
        private bool changed;
        public Guid WorldId => actual.WorldId;
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => actual.Enumerate(maxEntries);
        public byte[] Read(ArtifactAddress artifact)
        {
            var bytes = actual.Read(artifact);
            if (!changed && artifact.RelativePath == "packs/P/pack.mcmeta")
            { changed = true; Put(root, artifact.RelativePath, Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":55,\"description\":\"Changed\"}}")); }
            return bytes;
        }
    }
    public static void DirectoryMetadataRaceIsVisible()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "packs/P/pack.mcmeta", Encoding.UTF8.GetBytes(ResourcePack));
            Put(root, "packs/P/assets/models/lang/en_us.json", Encoding.UTF8.GetBytes("{\"key\":\"Text\"}"));
            var result = WorldDiscovery.Discover(new MetadataRace(new WorldInventory(root, World), root), new(SnapshotScope.All));
            Require(!result.Complete && result.Sources.Any(source => source.Location.Artifact.RelativePath == "packs/P/pack.mcmeta" && source.DiagnosticCode == "PackMetadataChanged")
                && result.Sources.Where(source => source.Location.PackRoot == "packs/P").All(source => source.Outcome != SourceOutcome.Success),
                "metadata version and metadata raw hash cannot come from different reads; affected pack sources stay incomplete");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void DirectoryAndArchiveStructures()
    {
        foreach (var (version, format, folder) in new[] { (3700, 26, "structures"), (3953, 48, "structure") })
        {
            var root = ScratchDirectory.CreateChild();
            try
            {
                Level(root, false);
                var block = C(E("pos", L(new NbtInt(0), new NbtInt(0), new NbtInt(0))), E("state", new NbtInt(0)),
                    E("nbt", C(E("id", S("minecraft:chest")), E("CustomName", Text("Container", false)))));
                var entity = C(E("pos", L(new NbtDouble(0), new NbtDouble(0), new NbtDouble(0))), E("nbt", C(E("id", S("minecraft:zombie")), E("CustomName", Text("Entity", false)))));
                var structure = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("size", L(new NbtInt(1), new NbtInt(1), new NbtInt(1))),
                    E("palette", L(C(E("Name", S("minecraft:chest"))))), E("blocks", L(block)), E("entities", L(entity))));
                var bytes = CompressedNbt.WriteDocument(structure, NbtCompression.GZip);
                var metadata = System.Text.Json.JsonSerializer.Serialize(new { pack = new { pack_format = format, description = "Data" } });
                var entry = "data/models/" + folder + "/room.nbt";
                Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata)); Put(root, "datapacks/p/" + entry, bytes);
                using (var stream = new MemoryStream())
                {
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        using (var content = zip.CreateEntry("pack.mcmeta").Open()) content.Write(Encoding.UTF8.GetBytes(metadata));
                        using (var content = zip.CreateEntry(entry).Open()) content.Write(bytes);
                    }
                    Put(root, "arbitrary-structures.zip", stream.ToArray());
                }
                var scan = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], ["container.name", "entity.name"], [])));
                var rows = scan.ReadSources().ToArray(); var texts = rows.SelectMany(row => row.Text?.Occurrences ?? []).ToArray();
                Require(rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant) && texts.Length == 4
                    && texts.Count(text => Row(text) == "/blocks/0/nbt/CustomName|/text|container.name|Container") == 2
                    && texts.Count(text => Row(text) == "/entities/0/nbt/CustomName|/text|entity.name|Entity") == 2
                    && texts.All(text => text.Address.Chunk is null && text.Address.Dimension is null)
                    && texts.Any(text => text.Address.Artifact.RelativePath == "arbitrary-structures.zip" && text.Address.Artifact.ContainerEntry == entry)
                    && texts.Any(text => text.Address.Artifact.RelativePath == "datapacks/p/" + entry),
                    "Java structure NBT in real directory/ZIP packs keeps own artifact version and embedded component paths, without invented world coordinates");
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
