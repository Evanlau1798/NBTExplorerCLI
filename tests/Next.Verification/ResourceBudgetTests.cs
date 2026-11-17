using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class ResourceBudgetTests
{
    public static void AggregateInputAndOutputCatalogBudgetsRefuseBeforeReadingPayloads()
    {
        foreach (var capture in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var inventory = new OversizedInventory(new WorldInventory(root, World), capture);
                var objects = new FileSystemWorldBackupStore(state); var refused = false;
                try
                {
                    if (capture)
                        _ = new ResourceInputService(objects).Capture(new(World, ResourceOutputFormat.ZipPack, "resources.zip", [new("zh_tw", [])], new(34, 0)), inventory);
                    else
                        _ = new ResourceOutputService(inventory, new ArtifactStorage(root, World), objects).Build(new(World, ResourceOutputFormat.LanguageFiles, "", [new("en_us", []), new("zh_tw", [])]));
                }
                catch (Next.Domain.CodecException error) when (error.Code == CodecError.LimitExceeded) { refused = true; }
                Require(refused && inventory.Reads == 0, "oversized complete catalog refuses before payload reads: capture=" + capture);
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    private sealed class OversizedInventory(WorldInventory inner, bool capture) : IWorldInventory, IResourceOutputScope
    {
        public int Reads { get; private set; }
        public Guid WorldId => inner.WorldId;
        public Hash256? AccessBinding => inner.AccessBinding;
        public IDisposable GuardResourceOutput(ResourceGeneration generation) => inner.GuardResourceOutput(generation);
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => new(capture
            ? [new("pack.mcmeta", 100, false, SourceOutcome.Success), new("assets/test/a.bin", 40_000_000, false, SourceOutcome.Success), new("assets/test/b.bin", 40_000_000, false, SourceOutcome.Success)]
            : [new("en_us.json", 40_000_000, false, SourceOutcome.Success), new("zh_tw.json", 40_000_000, false, SourceOutcome.Success)]);
        public byte[] Read(ArtifactAddress artifact) { Reads++; throw new InvalidOperationException("Payload must not be read after an oversized catalog"); }
    }
}
