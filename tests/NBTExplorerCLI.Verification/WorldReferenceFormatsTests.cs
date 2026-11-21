using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using System.IO.Compression;
using System.Text;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldReferenceFormatsTests
{
    internal static readonly VerificationCase[] FormatCases =
    [
        new("reference-world-archive-functions", "reference-world", _ => ArchiveFunctionsKeepUnselectedEntries()),
        new("reference-world-native-nbt-profiles", "reference-world", _ => NativeNbtProfilesKeepTheirPhysicalLayout()),
        new("reference-world-proven-storage", "reference-world", _ => ProvenStorageKeepsReferenceCoverage(saved: false)),
        new("reference-world-proven-saved-storage", "reference-world", _ => ProvenStorageKeepsReferenceCoverage(saved: true)),
        new("reference-world-proven-native-book-storage", "reference-world", _ => ProvenStorageKeepsReferenceCoverage(saved: false, book: true)),
    ];

    private static ReferencePatch Operation(string root, string state, out WorldQuerySnapshot snapshot, out WorldInventory inventory)
    {
        snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache")); inventory = new(root, World);
        Require(snapshot.Manifest.CoverageComplete, "all format fixture sources have complete actual extraction");
        return new WorldReferenceService(inventory).Create(snapshot, "format-group", WorldReferenceTests.Names(snapshot));
    }
    public static void ArchiveFunctionsKeepUnselectedEntries()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("tellraw @a[name=Guard] \"player\""));
            const string script = "execute as @e[type=armor_stand,name=Guard] run tellraw @s {\"text\":\"Guard\"}";
            const string keep = "tellraw @a[name=Guard] \"keep\"";
            var original = Zip(("pack.mcmeta", Format48Pack), ("data/test/function/rename.mcfunction", script), ("data/test/function/keep.mcfunction", keep));
            Put(root, "datapacks/ref.zip", original);
            var operation = Operation(root, state, out var snapshot, out var inventory);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            Require(plan.Outputs.Length == 3 && plan.Outputs.Any(output => output.Artifact.RelativePath == "datapacks/ref.zip"), "the outer ZIP is one of exactly three promised physical outputs");
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            Require(WorldApplyTests.Engine(root, state).Apply(prepared, true).Success, "archive reference group readback succeeds");
            using var archive = new ZipArchive(new MemoryStream(File.ReadAllBytes(Path.Combine(root, "datapacks/ref.zip"))), ZipArchiveMode.Read);
            string Read(string name) { using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8); return reader.ReadToEnd(); }
            Require(Read("data/test/function/rename.mcfunction") == script.Replace("name=Guard", "name=\"Sentinel\"", StringComparison.Ordinal)
                && Read("data/test/function/keep.mcfunction") == keep && Read("pack.mcmeta") == Format48Pack, "only the declared selector name token changes inside the correct ZIP entry");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void ProvenStorageKeepsReferenceCoverage(bool saved, bool book = false)
    {
        var root = book ? ScratchDirectory.CreateChild() : WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var writer = book ? "datapacks/p/data/proof/functions/write.mcfunction" : WorldQueryTests.Function(1);
            var reader = book ? "datapacks/p/data/proof/functions/read.mcfunction" : WorldQueryTests.Function(2);
            var producer = saved ? "" : """data modify storage proof:messages body set value '{"text":"Welcome"}'""";
            var display = """tellraw @e[type=armor_stand,name=Guard] {"nbt":"body","storage":"proof:messages","interpret":true}""";
            if (book)
            {
                Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
                var actor = new NbtCompound([new("id", new NbtString("minecraft:armor_stand")), new("CustomName", new NbtString("{\"text\":\"Guard\"}"))]);
                var seed = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3465)), new("Entities", new NbtList(NbtType.Compound, [actor]))]));
                Put(root, WorldReferenceTests.Entities, RegionFile.Read(new byte[8192]).WithChunk(0, seed, NbtCompression.Zlib, 17));
                WriteBook("""{"nbt":"body","storage":"proof:messages","interpret":true}""");
                display = "tellraw @e[type=armor_stand,name=Guard] \"Ready\"";
            }
            if (saved)
            {
                display = display.Replace("\"body\"", "\"body[]\"", StringComparison.Ordinal);
                var body = new NbtList(NbtType.String, [new NbtString("{\"text\":\"Welcome\"}")]);
                var contents = new NbtCompound([new("messages", new NbtCompound([new("body", body)])),
                    new("idle", new NbtCompound([new("memo", new NbtString("{\"text\":\"Unused body\"}"))]))]);
                var data = new NbtCompound([new("contents", contents)]);
                var seed = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3953)), new("data", data)]));
                Put(root, "data/command_storage_proof.dat", CompressedNbt.WriteDocument(seed, NbtCompression.GZip));
            }
            Put(root, writer, Encoding.UTF8.GetBytes(producer));
            Put(root, reader, Encoding.UTF8.GetBytes(display));
            var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var operation = Operation(root, state, out var snapshot, out var inventory);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            var expectedMembers = book ? 2 : 4;
            Require(operation.Synchronization.Members.Length == expectedMembers && plan.Outputs.Length == expectedMembers,
                "a proven storage display also keeps the exact selected entity name and selector members");
            var storage = new ArtifactStorage(root, World);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state, storage);
            Require(engine.Apply(prepared, true).Success && File.ReadAllText(Path.Combine(root, writer)) == producer
                && File.ReadAllText(Path.Combine(root, reader)) == display.Replace("name=Guard", "name=\"Sentinel\"", StringComparison.Ordinal),
                "reference changes preserve the storage operand, producer, and unrelated support statements");
            Require(engine.Recover(plan.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 1
                && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "the group rolls back byte-for-byte");
            if (book)
            {
                WriteBook("""{"nbt":"body","storage":"proof:messages","interpret":true,"hoverEvent":{"action":"show_entity","contents":{"type":"minecraft:pig","id":"00000000-0000-0000-0000-000000000001"}}}""");
                var opaque = WorldQueryTests.Query(root, Path.Combine(state, "cache"));
                Require(opaque.Manifest.CoverageComplete, "reading world text can still be complete with an unresolved native reference");
                Reject<EditRejectedException>(() => new WorldReferenceService(inventory).Create(opaque, "opaque", WorldReferenceTests.Names(opaque)));
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }

        void WriteBook(string page)
        {
            var entityFile = Path.Combine(root, WorldReferenceTests.Entities);
            var region = RegionFile.Read(File.ReadAllBytes(entityFile));
            var document = region.ReadChunk(0).Document;
            var path = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0)]);
            var actor = (NbtCompound)NbtLocator.Resolve(document.Root, path).Value!;
            var tag = new NbtCompound([new("pages", new NbtList(NbtType.String, [new NbtString(page)]))]);
            var written = new NbtCompound([new("id", new NbtString("minecraft:written_book")), new("Count", new NbtByte(1)), new("tag", tag)]);
            var hand = new NbtEntry("HandItems", new NbtList(NbtType.Compound, [written, new NbtCompound([])]));
            var changed = TypedTreeEdits.Apply(document, [TypedEdit.Replace(path, actor,
                new NbtCompound(actor.Entries.Where(field => field.Name != "HandItems").Append(hand)))]);
            File.WriteAllBytes(entityFile, region.WithChunk(0, changed, NbtCompression.Zlib, 17));
        }
    }
    public static void NativeNbtProfilesKeepTheirPhysicalLayout()
    {
        foreach (var version in new[] { 4325, 4774 })
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(version, version == 4774 ? null : WorldMetadataTests.Dimensions), NbtCompression.GZip));
                if (version == 4774) Put(root, "data/minecraft/world_gen_settings.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Separate(version, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(Format48Pack));
                Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("execute as @e[type=armor_stand,name=Guard] run tellraw @s {\"text\":\"Guard\"}"));
                var prefix = version == 4774 ? "dimensions/minecraft/overworld/" : "";
                var entities = prefix + "entities/r.0.0.mca"; var terrain = prefix + "region/r.0.0.mca";
                var name = new NbtCompound([new("text", new NbtString("Guard")), new("color", new NbtString("red"))]);
                var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(version)), new("Entities", new NbtList(NbtType.Compound, [new NbtCompound([
                    new("id", new NbtString("minecraft:armor_stand")), new("CustomName", name)])]))]));
                Put(root, entities, RegionFile.Read(new byte[8192]).WithChunk(0, document, NbtCompression.Zlib, 21));
                var sign = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(version)), new("block_entities", new NbtList(NbtType.Compound, [new NbtCompound([
                    new("id", new NbtString("minecraft:sign")), new("front_text", new NbtCompound([new("messages", new NbtList(NbtType.Compound, [new NbtCompound([new("selector", new NbtString("@e[type=armor_stand,name=Guard]"))])]))]))])]))]));
                Put(root, terrain, RegionFile.Read(new byte[8192]).WithChunk(0, sign, NbtCompression.GZip, 22));
                var operation = Operation(root, state, out var snapshot, out var inventory); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
                using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
                Require(WorldApplyTests.Engine(root, state).Apply(prepared, true).Success, "native NBT profile group commits in its actual world layout");
                var actual = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, entities))).ReadChunk(0).Document;
                var path = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")]);
                Require(NbtLocator.Resolve(actual.Root, path).Value == new NbtCompound([new("text", new NbtString("Sentinel")), new("color", new NbtString("red"))]), "native tree paths and protected component style survive");
                Require(RegionFile.Read(File.ReadAllBytes(Path.Combine(root, entities))).Chunks[0].Timestamp == 21, "region timestamp remains unchanged");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void StoredMinecartCommandsAreRealReferenceMembers()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, WorldReferenceTests.Entities); var region = RegionFile.Read(File.ReadAllBytes(path)); var original = region.ReadChunk(0).Document;
            var entities = (NbtList)NbtLocator.Resolve(original.Root, new([PathSegment.ForKey("Entities")])).Value!;
            var cart = new NbtCompound([new("id", new NbtString("minecraft:command_block_minecart")), new("Command", new NbtString("execute as @e[type=armor_stand,name=Guard] run tellraw @s \"cart\""))]);
            var changed = TypedTreeEdits.Apply(original, [TypedEdit.Replace(new([PathSegment.ForKey("Entities")]), entities, new NbtList(NbtType.Compound, entities.Items.Append(cart)))]);
            File.WriteAllBytes(path, region.WithChunk(0, changed, NbtCompression.Zlib, 17));
            var operation = Operation(root, state, out var snapshot, out var inventory);
            Require(operation.Synchronization.Members.Length == 4, "the stored command minecart contributes its actual selector token, not a no-reference claim");
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            Require(WorldApplyTests.Engine(root, state).Apply(prepared, true).Success, "native name and native command field share one physical rewrite");
            var actual = RegionFile.Read(File.ReadAllBytes(path)).ReadChunk(0).Document;
            var command = (NbtString)NbtLocator.Resolve(actual.Root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(2), PathSegment.ForKey("Command")])).Value!;
            Require(command.Value.Contains("name=\"Sentinel\"", StringComparison.Ordinal) && command.Value.Contains("\"cart\"", StringComparison.Ordinal), "only the minecart's verified name reference changes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
}
