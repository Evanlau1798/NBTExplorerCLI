using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class KeyPlanningReadTests
{
    public static void AdditionalKeysInTheSameFunctionsDoNotRereadTheirSources()
    {
        const int functions = 8;
        const int slotsPerFile = 16;
        var root = ScratchDirectory.CreateChild();
        var cache = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false);
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            for (var file = 0; file < functions; file++)
            {
                var lines = Enumerable.Range(0, slotsPerFile).Select(slot => $"tellraw @s \"Source {file}/{slot}\"");
                Put(root, WorldQueryTests.Function(file), Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
            }
            var inventory = new CountingInventory(new WorldInventory(root, World));
            var request = new WorldQueryRequest(new(new SnapshotScope([], [], ["command.function"], [])), Execution: new(1));
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache))
                .QueryAsync(request).GetAwaiter().GetResult().Snapshot!;
            var translations = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Select(item =>
            {
                var slot = item.ToSlot("en_us", "zh_tw");
                return new LiteralTranslation(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "譯文:" + slot.SourceText));
            }).ToArray();
            Require(translations.Length == functions * slotsPerFile, "fixture must exercise every function and slot");
            var language = new ArtifactAddress(World, "resources.zip", ArtifactKind.Archive, "assets/test/lang/zh_tw.json");
            var conversions = new WorldKeyConversionService(inventory);
            var all = conversions.Create(snapshot, translations, language, "control.");
            var onePerFile = all.GroupBy(operation => operation.Conversion.Address.Artifact).Select(group => group.First()).ToArray();
            var builder = new WorldPlanBuilder(inventory);
            var before = inventory.FunctionReads;
            var minimal = builder.Build(snapshot, onePerFile);
            var minimalReads = inventory.FunctionReads - before;
            before = inventory.FunctionReads;
            var complete = builder.Build(snapshot, all);
            var completeReads = inventory.FunctionReads - before;
            Require(minimal.Operations.Length == functions && complete.Operations.Length == functions * slotsPerFile,
                "both plans must preserve their exact requested operation sets");
            Require(minimalReads > 0 && completeReads == minimalReads,
                $"function reads must scale with source files, not keys: one per file={minimalReads}, {slotsPerFile} per file={completeReads}");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    private sealed class CountingInventory(IWorldInventory inner) : IWorldInventory
    {
        public int FunctionReads { get; private set; }
        public Guid WorldId => inner.WorldId;
        public Hash256? AccessBinding => inner.AccessBinding;
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => inner.Enumerate(maxEntries);
        public byte[] Read(ArtifactAddress artifact)
        {
            if (artifact.Kind == ArtifactKind.McFunction) FunctionReads++;
            return inner.Read(artifact);
        }
    }
}
