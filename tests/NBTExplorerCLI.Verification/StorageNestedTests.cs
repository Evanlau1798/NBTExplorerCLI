using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageNestedTests
{
    internal static void BoundInterpretedDependencies()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        const string folder = "datapacks/p/data/proof/functions/";
        const string writer = folder + "message.mcfunction";
        const string wrapper = folder + "layout.mcfunction";
        const string reader = folder + "show.mcfunction";
        const string leaf = """data modify storage proof:body message set value '{"text":"Bienvenue"}'""";
        const string layout = """
            data modify storage proof:layout card set value '{"nbt":"message","storage":"proof:body","interpret":true,"color":"green"}'
            data modify storage proof:screen rendered set value '[{"nbt":"card","storage":"proof:layout","interpret":true}]'
            """;
        const string display = """tellraw @a {"nbt":"rendered","storage":"proof:screen","interpret":true}""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
            Put(root, writer, Encoding.UTF8.GetBytes(leaf));
            Put(root, wrapper, Encoding.UTF8.GetBytes(layout));
            Put(root, reader, Encoding.UTF8.GetBytes(display));
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "the world proves both interpreted hops from display to the source component");
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == writer).Text!.Occurrences
                .Single(item => item.Text == "Bienvenue").ToSlot("fr_fr", "de_de");
            var translation = new LiteralPatch("nested", new(slot, new(slot.Id, slot.SourceTextHash, "de_de", "Willkommen")));
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [translation]);
            var preparation = new WorldPlanPreparation(inventory, storage);
            var engine = WorldApplyTests.Engine(root, state, storage);
            const string late = folder + "raw.mcfunction";
            using (var rejected = preparation.Prepare(snapshot, plan, true))
            {
                Put(root, late, "data get storage proof:body message"u8.ToArray());
                Reject<ArtifactConflictException>(() => engine.Apply(rejected, true));
                Require(File.ReadAllText(Path.Combine(root, writer)) == leaf, "a late raw reader blocks the prepared plan before any rewrite");
                File.Delete(Path.Combine(root, late));
            }
            using var prepared = preparation.Prepare(snapshot, plan, true);
            Require(engine.Apply(prepared, true).Success, "the transitive producer can be translated");
            Require(File.ReadAllText(Path.Combine(root, writer)).Contains("Willkommen", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(root, wrapper)) == layout && File.ReadAllText(Path.Combine(root, reader)) == display,
                "the writer changes while both component readers keep their exact bytes");
            Require(engine.Recover(plan.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 1
                && File.ReadAllText(Path.Combine(root, writer)) == leaf, "rollback restores the source bytes");

            foreach (var blocked in new[]
            {
                layout + "\ndata get storage proof:body message",
                layout + "\ndata get storage proof:layout card",
                layout.Replace("\"interpret\":true", "\"interpret\":false", StringComparison.Ordinal),
                layout.Replace("\"color\":\"green\"", "\"font\":\"proof:unproven\"", StringComparison.Ordinal),
                layout + "\n" + """data modify storage proof:body message set value '{"nbt":"rendered","storage":"proof:screen","interpret":true}'""",
            })
            {
                Put(root, wrapper, Encoding.UTF8.GetBytes(blocked));
                Require(!WorldQueryTests.Query(root, cache).Manifest.CoverageComplete,
                    "raw observers, unproven font, and cyclic rendering cannot authorize the source");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }
}
