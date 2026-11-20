using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageFunctionBudgetTests
{
    internal static void CheckCatalogBudget()
    {
        var root = ScratchDirectory.CreateChild();
        var world = new WorldInventory(root, Guid.NewGuid());
        const string metadata = """{"pack":{"pack_format":15,"description":"Proof"}}""";
        const string folder = "datapacks/main/data/demo/functions/";
        var request = new ScanRequest(SnapshotScope.All, new(MaxDecodedBytes: 4096));
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/main/pack.mcmeta", System.Text.Encoding.UTF8.GetBytes(metadata));
            Put(root, folder + "write.mcfunction", """data modify storage demo:display lines append value '{"text":"Welcome"}'"""u8.ToArray());
            Put(root, folder + "show.mcfunction", """tellraw @a {"storage":"demo:display","nbt":"lines[]","interpret":true}"""u8.ToArray());
            Put(root, folder + "watch.mcfunction", "execute store success score #seen demo run function demo:target"u8.ToArray());
            Put(root, folder + "target.mcfunction", "scoreboard players add #count demo 1"u8.ToArray());
            for (var index = 0; index < 24; index++)
                Put(root, folder + $"unused{index}.mcfunction", "# remains outside the observed call graph"u8.ToArray());
            Put(root, folder + "unused_relay.mcfunction", "function demo:spare"u8.ToArray());
            for (var index = 0; index < 24; index++)
                Put(root, $"datapacks/unused{index}.zip", Zip(("pack.mcmeta", metadata), ("data/demo/functions/spare.mcfunction", "# called only by the unreachable relay")));
            var unrelated = Extract().ToArray();
            Require(unrelated.Any(row => row.Text?.Occurrences.Any(item => item.Text == "Welcome") == true)
                && !unrelated.Any(row => row.DiagnosticCode == "LimitExceeded"), "definitions unreachable from the observed calls must not exhaust the function proof budget");
            for (var index = 0; index < 24; index++)
                Put(root, $"datapacks/overlay{index}.zip", Zip(("pack.mcmeta", metadata), ("data/demo/functions/target.mcfunction", "# another target definition")));
            Require(Extract().Any(row => row.DiagnosticCode == "LimitExceeded"), "retained duplicate function definitions must count against the shared evidence budget");
        }
        finally { Directory.Delete(root, true); }

        IEnumerable<TextSourceExtraction> Extract() => new WorldTextExtraction(world, request).ReadSources();
    }
}
