using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldCommandExtractionTests
{
    internal static void StaticTimeAndScheduling()
    {
        const string script = """
            summon armor_stand
            summon minecraft:armor_stand ~ ~.5 ~
            schedule function demo:repeat 60t
            schedule function demo:repeat .5s replace
            schedule clear demo:repeat
            difficulty peaceful
            time set 13000
            time add 2s
            time set 0.5t
            time set 2147483647t
            bossbar remove boss:status
            title @a times 0t 40t 10t
            execute as @e[type=armor_stand,name="North Guard"] run title @s actionbar {"text":"Welcome"}
            """;
        var address = new SourceAddress(new(World, "controls.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var result = CommandTextExtractor.Extract(new(address, script), profile);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Welcome" && result.References.Single().Name == "North Guard",
            "static commands are complete without losing adjacent text or selector names");
        foreach (var invalid in new[] { "summon armor_stand ~ ~", "summon armor_stand ^ ~ ~", "schedule function demo:repeat 0t",
            "schedule function demo:repeat .01s", "schedule function demo:repeat 2x", "schedule function demo:repeat 2t unexpected",
            "schedule clear demo:repeat trailing", "difficulty impossible", "time set -2", "bossbar remove boss:status trailing",
            "title @a times 0t 40.0x 10t", "title @a times -1 40 10" })
            Require(!CommandTextExtractor.Extract(new(address, invalid), profile).Complete, $"invalid input stays unresolved: {invalid}");
        HistoricalTimingCoverage();
    }

    private static void HistoricalTimingCoverage()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string folder = "datapacks/p/data/demo/functions/";
        var commands = new[] { ("time set 13000", 0), ("time set 2s", 1901), ("schedule function demo:repeat 20t", 1901),
            ("schedule function demo:repeat 20t append", 2205), ("schedule clear demo:repeat", 2205), ("title @a times 0t 40t 10t", 3320) };
        try
        {
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":4,"description":"Test"}}"""u8.ToArray());
            foreach (var (item, index) in commands.Select((item, index) => (item, index)))
                Put(root, folder + index + ".mcfunction", Encoding.UTF8.GetBytes(item.Item1));
            foreach (var version in new[] { 1631, 1900, 1901, 2204, 2205, 3218, 3319, 3320 })
            {
                Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(version, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                var rows = WorldQueryTests.Query(root, cache).ReadSources().ToDictionary(row => row.Source.Location.Artifact.RelativePath);
                foreach (var (item, index) in commands.Select((item, index) => (item, index)))
                    Require(rows[folder + index + ".mcfunction"].Outcome == (version >= item.Item2 ? SourceOutcome.Irrelevant : SourceOutcome.Unsupported),
                        $"runtime {version} must respect the syntax boundary for {item.Item1} with a shared pack format and cache");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    public static void FunctionsAndPhysicalNbtCommands()
    {
        CommandsRespectGrammarTransitions();
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(Format48Pack));
            Put(root, "datapacks/p/data/test/predicate/gate.json", """{"condition":"minecraft:location_check","predicate":{"dimension":"minecraft:overworld"}}"""u8.ToArray());
            const string functionBody = """
                damage @s 2 magic by @e[limit=1,tag=anchor]
                fill ~ ~ ~ ~ ~ ~ light[level=3] keep
                item modify entity @s armor.head test:restore
                data get storage test:state stages[{Progress:2b}].Progress
                data modify storage test:state copies append from entity @s HandItems[{id:"minecraft:paper"}]
                data modify storage test:state {} merge value {phase:3b}
                execute if blocks 0 64 0 0 64 0 1 64 0 all if predicate test:gate run tellraw @a[name=Guard] [{"text":"Function"}]
                execute if data storage test:state stages[{Progress:2b}] run return 7
                execute unless data entity @s {Item:{id:"minecraft:barrier"}} run return 1
                """;
            Put(root, "datapacks/p/data/test/function/main.mcfunction", Encoding.UTF8.GetBytes(functionBody));
            Put(root, "datapacks/p/data/test/function/dynamic.mcfunction", Encoding.UTF8.GetBytes("$tellraw @s $(message)\n"));
            Put(root, "datapacks/p/data/test/function/invalid.mcfunction", Encoding.UTF8.GetBytes("tellraw @s {\"selector\":42}\n"));
            Put(root, "datapacks/p/data/test/function/unsafe.mcfunction",
                """execute if data entity @s {Item:{tag:{display:{Name:'Guard'}}}} run return 1"""u8.ToArray());
            var block = C(E("id", S("minecraft:command_block")), E("Command", S("title @a[name=!Guard] title {\"text\":\"Block\"}")));
            var item = C(E("id", S("minecraft:command_block")), E("components", C(E("minecraft:block_entity_data", block))));
            Put(root, "playerdata/p.dat", CompressedNbt.WriteDocument(new("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item)))), NbtCompression.GZip));
            var regionBlock = C(E("id", S("minecraft:command_block")), E("Command", S("tellraw @a[name=RegionGuard] \"Region\"")));
            var region = RegionFile.Read(new byte[8192]).WithChunk(31, new("", C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(regionBlock)))), NbtCompression.Zlib, 17);
            const string regionPath = "DIM-1/region/r.-1.0.mca"; Put(root, regionPath, region);
            var rows = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All)).ReadSources().ToArray();
            var function = rows.Single(row => row.Source.Location.Artifact.RelativePath.EndsWith("/main.mcfunction", StringComparison.Ordinal));
            var player = rows.Single(row => row.Source.Location.Artifact.RelativePath == "playerdata/p.dat");
            Require(function.Outcome == SourceOutcome.Success && function.Text!.Complete && function.Text.Occurrences.Single().Text == "Function"
                && player.Outcome == SourceOutcome.Success && player.Text!.Complete && player.Text.Occurrences.Single().Text == "Block"
                && player.Text.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/Inventory/0/components/minecraft:block_entity_data/Command", "real function and own-version DAT pending commands route through the same exact grammar service");
            Require(function.Commands!.References.Single().Name == "Guard" && !function.Commands.References.Single().Negated && player.Commands!.References.Single().Negated,
                "source outcomes retain separate reference candidates for functions and physical NBT commands");
            var regionRow = rows.Single(row => row.Source.Location.Artifact.RelativePath == regionPath && row.Source.Chunk is not null);
            Require(regionRow.Outcome == SourceOutcome.Success && regionRow.Commands!.References.Single().Name == "RegionGuard" && regionRow.Text!.Occurrences.Single().Text == "Region"
                && regionRow.Text.Occurrences.Single().Address.Chunk == new ChunkPosition(-1, 0) && regionRow.Text.Occurrences.Single().Address.Dimension == "minecraft:the_nether"
                && regionRow.Text.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/block_entities/0/Command"
                && Hash256.Compute(File.ReadAllBytes(Path.Combine(root, regionPath))) == Hash256.Compute(region), "real Zlib Region chunk commands retain exact dimension/negative chunk and physical path; source bytes remain unchanged");
            var dynamic = rows.Single(row => row.Source.Location.Artifact.RelativePath.EndsWith("/dynamic.mcfunction", StringComparison.Ordinal));
            Require(dynamic.Outcome == SourceOutcome.Unsupported && dynamic.DiagnosticCode == "DynamicCommandMacro" && dynamic.Commands!.ReferenceCompleteness == ReferenceCompleteness.Unresolved,
                "actual adapter cannot promote unknown macro to complete or erase reference incompleteness");
            var invalid = rows.Single(row => row.Source.Location.Artifact.RelativePath.EndsWith("/invalid.mcfunction", StringComparison.Ordinal));
            Require(invalid.Outcome == SourceOutcome.Failed && invalid.DiagnosticCode == "InvalidComponent" && invalid.Text!.Occurrences.IsEmpty,
                "actual function adapter preserves invalid selector type as a source failure instead of irrelevant empty success");
            var unsafeMatch = rows.Single(row => row.Source.Location.Artifact.RelativePath.EndsWith("/unsafe.mcfunction", StringComparison.Ordinal));
            Require(unsafeMatch.Outcome == SourceOutcome.Unsupported && unsafeMatch.DiagnosticCode == "UnknownExecuteGrammar",
                "a root item predicate with a display name still requires reference analysis");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void CommandsRespectGrammarTransitions()
    {
        var source = new SourceAddress(new(World, "rules.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var native = new SourceAddress(new(World, "data/scoreboard.dat", ArtifactKind.Nbt), TypedPath.Root, "scoreboard.objective.name");
        var itemSelector = SelectorNames.Parse("""@e[type=item,nbt={Item:{id:"minecraft:paper"}}]""");
        var namedItemSelector = SelectorNames.Parse("""@e[type=item,nbt={Item:{tag:{display:{Name:'Guard'}}}}]""");
        Require(itemSelector.Complete && !namedItemSelector.Complete, "a stored item ID can be matched without authorizing display-name filters");
        foreach (var (version, itemCondition, nearest) in new[]
        {
            (3465, false, false), (3820, false, false), (3821, true, false), (3945, true, false), (3946, true, true), (3953, true, true)
        })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            var items = CommandTextExtractor.Extract(new(source, "execute if items entity @s weapon.mainhand minecraft:diamond"), profile).Complete;
            var select = CommandTextExtractor.Extract(new(source, "damage @n 2"), profile).Complete;
            var component = NativeComponentReferences.EmbeddedJson(native, """{"selector":"@n[name=Guard]"}""", profile).Completeness == ReferenceCompleteness.Complete;
            Require(items == itemCondition && select == nearest && component == nearest,
                $"grammar transition at {version}: expected item/selector/component {itemCondition}/{nearest}/{nearest}, got {items}/{select}/{component}");
        }
        foreach (var (format, kind, expected) in new[] { (33, PackSourceKind.Datapack, false), (34, PackSourceKind.Datapack, true), (28, PackSourceKind.ResourcePack, false) })
        {
            var metadata = PackMetadata.Read(Encoding.UTF8.GetBytes($$$"""{"pack":{"pack_format":{{{format}}},"description":"Test"}}"""), kind);
            var profile = MinecraftProfiles.ForPackMetadata(metadata)!;
            Require(CommandTextExtractor.Extract(new(source, "execute if items entity @s armor.* *"), profile).Complete == expected,
                "a pack declaration must establish the item condition grammar before allowing slot ranges");
        }
    }
}
