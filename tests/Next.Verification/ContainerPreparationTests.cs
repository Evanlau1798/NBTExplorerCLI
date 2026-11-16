using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.NbtItemTextTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class ContainerPreparationTests
{
    private static LiteralPatch[] Translate(WorldQuerySnapshot snapshot, Func<TextOccurrence, bool> select)
        => snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(select).Select((item, index) =>
        {
            var slot = item.ToSlot("en_us", "zh_tw"); return new LiteralPatch("literal-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "譯:" + slot.SourceText)));
        }).ToArray();
    private static void NoOpPreservesRawBytes(string root, WorldQuerySnapshot snapshot, IReadOnlyList<LiteralPatch> operations)
    {
        var inventory = new WorldInventory(root, World);
        var unchanged = operations.Select(operation => new LiteralPatch(operation.Id, new(operation.Translation.Slot, operation.Translation.Reply with { Text = operation.Translation.Slot.SourceText })));
        using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, new WorldPlanBuilder(inventory).Build(snapshot, unchanged), true);
        Require(prepared.Manifest.Artifacts.All(item => File.ReadAllBytes(item.CandidatePath!).SequenceEqual(File.ReadAllBytes(Path.Combine(root, item.Artifact.RelativePath)))), "no-op translation preserves complete original framing, compression, syntax spelling and container bytes");
    }
    public static void NbtCommandsAndNestedJsonUseExactLayers()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var block = C(E("id", S("command_block")), E("Command", S("tellraw @a[name=Guard] [\"One\",\"Two\"]")));
            var item = C(E("id", S("command_block")), E("components", C(E("block_entity_data", block))));
            const string player = "playerdata/p.dat";
            Put(root, player, CompressedNbt.WriteDocument(new("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item)))), NbtCompression.GZip));
            Put(root, "datapacks/old/pack.mcmeta", "{\"pack\":{\"pack_format\":26,\"description\":\"Old\"}}"u8.ToArray());
            const string path = "datapacks/old/data/test/loot_tables/book.json";
            const string tag = "{pages:['{\"text\":\"Page\",\"extra\":[\"Tail\"]}','{\"text\":\"Other\"}'],Protected:1b}";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "written_book", functions = new[] { new { function = "set_nbt", tag } } } } } }, number = 7 });
            Put(root, path, bytes);
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World);
            var operations = Translate(snapshot, item => item.Address.Artifact.RelativePath is player or path);
            Require(operations.Length == 5 && operations.Count(item => item.Translation.Slot.Address.ComponentPath is not null) == 3, "real DAT command and native JSON decoded SNBT/embeddedJSON expose five exact slots");
            var originals = new[] { player, path }.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(root, path)));
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, new WorldPlanBuilder(inventory).Build(snapshot, operations), true))
            {
                Require(prepared.Manifest.Artifacts.Length == 2, "nested syntax leaves share one candidate per physical file");
                foreach (var artifact in prepared.Manifest.Artifacts)
                {
                    var output = File.ReadAllBytes(artifact.CandidatePath!);
                    if (artifact.Artifact.RelativePath == player)
                    {
                        var nbt = CompressedNbt.ReadDocument(output, NbtCompression.GZip).Document;
                        var command = (NbtString)NbtLocator.Resolve(nbt.Root, operations.First(operation => operation.Translation.Slot.Address.Artifact.RelativePath == player).Translation.Slot.Address.NodePath).Value!;
                        Require(command.Value == "tellraw @a[name=Guard] [\"譯:One\",\"譯:Two\"]", "NBT command keeps its physical path and selector while both logical siblings change");
                    }
                    else
                    {
                        using var json = BoundedJson.ReadDecoded(output);
                        var actual = NativeJsonStrings.Read(json.RootElement.GetProperty("pools")[0].GetProperty("entries")[0].GetProperty("functions")[0].GetProperty("tag"));
                        const string expected = """{pages:["{\"text\":\"譯:Page\",\"extra\":[\"譯:Tail\"]}","{\"text\":\"譯:Other\"}"],Protected:1b}""";
                        Require(actual == expected && json.RootElement.GetProperty("number").GetInt32() == 7, "decoded SNBT splices preserve other SNBT bytes and outer JSON fields");
                    }
                }
            }
            Require(originals.All(item => File.ReadAllBytes(Path.Combine(root, item.Key)).SequenceEqual(item.Value)), "nested preparation never changes source files");
            NoOpPreservesRawBytes(root, snapshot, operations);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void RegionGroupsRetainDimensionAndOpaqueChunks()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var paths = new[] { "region/r.0.0.mca", "DIM-1/region/r.0.0.mca" }; var originals = new Dictionary<string, byte[]>();
            foreach (var path in paths)
            {
                var block = C(E("id", S("command_block")), E("Command", S("tellraw @s \"" + path + "\"")));
                var doc = new NbtDocument("", C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(block)), E("Counter", new NbtInt(7))));
                var bytes = RegionFile.Read(RegionFile.Read(RegionFile.Read(new byte[8192]).WithChunk(0, doc, NbtCompression.Zlib, 17)).WithChunk(1, doc, NbtCompression.GZip, 18)).WithChunk(2, doc, NbtCompression.GZip, 19);
                var other = RegionFile.Read(bytes).Chunks[1]; bytes[other.SectorOffset * 4096 + 4] = 99;
                originals.Add(path, bytes); Put(root, path, bytes);
            }
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World);
            var literals = Translate(snapshot, item => paths.Contains(item.Address.Artifact.RelativePath) && item.Address.Chunk is { Z: 0, X: 0 or 2 });
            Require(literals.Length == 4 && literals.Select(item => item.Translation.Slot.Address.Dimension).Distinct().Count() == 2, "same region names with two selected chunks in each dimension remain distinct with unrelated opaque chunks");
            var typed = literals.Select((item, index) => new TypedPatch("counter-" + index, new(item.Translation.Slot.Address.Artifact, new([PathSegment.ForKey("Counter")]), "typed.value", item.Translation.Slot.Address.Dimension, item.Translation.Slot.Address.Chunk), TypedEdit.Replace(new([PathSegment.ForKey("Counter")]), new NbtInt(7), new NbtInt(8))));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, literals.Cast<PatchOperation>().Concat(typed));
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                Require(prepared.Manifest.Artifacts.Length == 2, "all selected operations in a region form one physical candidate");
                foreach (var artifact in prepared.Manifest.Artifacts)
                {
                    var before = RegionFile.Read(originals[artifact.Artifact.RelativePath]); var bytes = File.ReadAllBytes(artifact.CandidatePath!); var after = RegionFile.Read(bytes);
                    Require(after.Chunks[1] == before.Chunks[1] && after.CompressedPayload(1).SequenceEqual(before.CompressedPayload(1))
                        && bytes.AsSpan(8192, originals[artifact.Artifact.RelativePath].Length - 8192).SequenceEqual(originals[artifact.Artifact.RelativePath].AsSpan(8192)), "unselected opaque chunk descriptor, compressed bytes, padding and all old sectors remain identical");
                    foreach (var index in new[] { 0, 2 })
                    {
                        var doc = after.ReadChunk(index).Document;
                        Require(NbtLocator.Resolve(doc.Root, new([PathSegment.ForKey("Counter")])).Value == new NbtInt(8)
                            && ((NbtString)NbtLocator.Resolve(doc.Root, literals.Single(item => item.Translation.Slot.Address.Artifact == artifact.Artifact && item.Translation.Slot.Address.Chunk == new ChunkPosition(index, 0)).Translation.Slot.Address.NodePath).Value!).Value.Contains("譯:", StringComparison.Ordinal), "typed and command edits are verified inside each exact selected chunk");
                    }
                }
            }
            Require(originals.All(item => File.ReadAllBytes(Path.Combine(root, item.Key)).SequenceEqual(item.Value)), "region staging preserves both original dimension files");
            NoOpPreservesRawBytes(root, snapshot, literals);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void ArchiveEntriesShareOneCandidateAndPreserveOpaqueRecords()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string path = "datapacks/archive.zip";
            const string function = "data/test/function/a.mcfunction";
            const string json = "data/test/advancement/a.json";
            var original = Zip(("pack.mcmeta", LegacyPack), (function, "tellraw @s [\"One\",\"Two\"]\n"), (json, "{\"display\":{\"title\":{\"text\":\"Title\"}}}"), ("opaque.bin", "raw unchanged 雪"));
            Put(root, path, original);
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World);
            var operations = Translate(snapshot, item => item.Address.Artifact.RelativePath == path && item.Address.Artifact.ContainerEntry is function or json);
            Require(operations.Length == 3, "archive command and nativeJSON slots carry entry addresses with independent syntax binding hashes");
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            Require(plan.Outputs.Length == 1 && plan.Outputs[0].Artifact.ContainerEntry is null && plan.Preconditions.Any(item => item.Artifact.RelativePath == path && item.Image.Hash == Hash256.Compute(original)), "entry operations bind the physical ZIP preimage and exactly one output");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var bytes = File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!);
                using var archive = ZipArchiveValidation.OpenRead(bytes);
                string Read(string entry) { using var stream = archive.GetEntry(entry)!.Open(); using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd(); }
                Require(Read(function) == "tellraw @s [\"譯:One\",\"譯:Two\"]\n" && Read(json) == "{\"display\":{\"title\":{\"text\":\"譯:Title\"}}}"
                    && Read("pack.mcmeta") == LegacyPack && Read("opaque.bin") == "raw unchanged 雪", "both selected entries change exactly while unselected metadata and binary payload stay identical");
                var localBytes = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(original.Length - 6)));
                Require(bytes.AsSpan(0, localBytes).SequenceEqual(original.AsSpan(0, localBytes)), "all original local records, compressed streams, timestamps and extra fields are byte-identical");
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "archive candidate disposal does not replace original ZIP");
            NoOpPreservesRawBytes(root, snapshot, operations);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void NativeEncodedJsonArchiveNbtAndLanguageProfiles()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string jsonPath = "datapacks/p/data/test/item_modifier/encoded.json";
            const string raw = """{"function":"set_components","components":{"block_entity_data":{"id":"chest","CustomName":"{\"text\":\"Container\"}"}},"n":1e+02}""";
            Put(root, jsonPath, Encoding.UTF8.GetBytes(raw));
            const string zipPath = "datapacks/structures.zip"; const string entry = "data/test/structure/a.nbt";
            var block = C(E("nbt", C(E("id", S("chest")), E("CustomName", S("{\"text\":\"Chest\"}")))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(3953)), E("blocks", L(block))));
            using (var stream = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using (var content = zip.CreateEntry("pack.mcmeta").Open()) content.Write(Encoding.UTF8.GetBytes(LegacyPack));
                    using (var content = zip.CreateEntry(entry).Open()) content.Write(CompressedNbt.WriteDocument(document, NbtCompression.GZip));
                }
                Put(root, zipPath, stream.ToArray());
            }
            const string languageZip = "arbitrary-resources.zip";
            Put(root, languageZip, Zip(("pack.mcmeta", ResourcePack), ("assets/test/lang/en_us.json", "{\"key\":\"Hello %1$s\"}")));
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World);
            var operations = Translate(snapshot, item => item.Address.Artifact.RelativePath == jsonPath || item.Address.Artifact.RelativePath == zipPath && item.Address.Artifact.ContainerEntry == entry || item.Address.Artifact.RelativePath == languageZip && item.Address.SemanticRole == "language.value");
            Require(operations.Length == 3, "raw embedded JSON, zipped structure NBT and resource language use actual schema profiles");
            var language = operations.Single(operation => operation.Translation.Slot.Role == "language.value").Translation.Slot;
            Reject<SessionReplyRejectedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("bad-format", new(language, new(language.Id, language.SourceTextHash, "zh_tw", "Missing argument")))]));
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, new WorldPlanBuilder(inventory).Build(snapshot, operations), true))
                foreach (var artifact in prepared.Manifest.Artifacts)
                {
                    var bytes = File.ReadAllBytes(artifact.CandidatePath!);
                    if (artifact.Artifact.RelativePath == jsonPath)
                    {
                        using var json = BoundedJson.ReadDecoded(bytes);
                        Require(NativeJsonStrings.Read(json.RootElement.GetProperty("components").GetProperty("block_entity_data").GetProperty("CustomName")) == "{\"text\":\"譯:Container\"}"
                            && Encoding.UTF8.GetString(bytes).EndsWith(",\"n\":1e+02}", StringComparison.Ordinal), "native embedded component changes only its exact string token");
                    }
                    else
                    {
                        using var zip = ZipArchiveValidation.OpenRead(bytes);
                        if (artifact.Artifact.RelativePath == zipPath)
                        {
                            using var content = zip.GetEntry(entry)!.Open(); using var encoded = new MemoryStream(); content.CopyTo(encoded);
                            var actual = CompressedNbt.ReadDocument(encoded.ToArray(), NbtCompression.GZip).Document;
                            var value = (NbtString)NbtLocator.Resolve(actual.Root, new([PathSegment.ForKey("blocks"), PathSegment.ForIndex(0), PathSegment.ForKey("nbt"), PathSegment.ForKey("CustomName")])).Value!;
                            Require(value.Value == "{\"text\":\"譯:Chest\"}", "archive structure NBT readback verifies its exact embedded text tree");
                        }
                        else
                        {
                            using var content = zip.GetEntry("assets/test/lang/en_us.json")!.Open(); using var text = new StreamReader(content);
                            Require(text.ReadToEnd() == "{\"key\":\"譯:Hello %1$s\"}", "language entry keeps its key and required percent argument");
                        }
                    }
                }
            NoOpPreservesRawBytes(root, snapshot, operations);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
