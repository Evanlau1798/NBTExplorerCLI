using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.NbtItemTextTests;

namespace Next.Verification;

internal static class WorldCommandExtractionTests
{
    public static void FunctionsAndPhysicalNbtCommands()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            Put(root, "datapacks/p/data/test/function/main.mcfunction", Encoding.UTF8.GetBytes("tellraw @a[name=Guard] [{\"text\":\"Function\"}]\n"));
            Put(root, "datapacks/p/data/test/function/dynamic.mcfunction", Encoding.UTF8.GetBytes("$tellraw @s $(message)\n"));
            Put(root, "datapacks/p/data/test/function/invalid.mcfunction", Encoding.UTF8.GetBytes("tellraw @s {\"selector\":42}\n"));
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
        }
        finally { Directory.Delete(root, true); }
    }
}
