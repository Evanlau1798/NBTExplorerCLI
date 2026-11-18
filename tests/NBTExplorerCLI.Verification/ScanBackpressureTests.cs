using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class ScanBackpressureTests
{
    private sealed class ObservedInventory(IWorldInventory inner) : IWorldInventory
    {
        internal bool Armed;
        internal int FunctionReads;
        public Guid WorldId => inner.WorldId;
        public Hash256? AccessBinding => inner.AccessBinding;
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => inner.Enumerate(maxEntries);
        public byte[] Read(ArtifactAddress artifact)
        {
            if (Armed && artifact.RelativePath.EndsWith(".mcfunction", StringComparison.Ordinal)) Interlocked.Increment(ref FunctionReads);
            return inner.Read(artifact);
        }
    }

    public static void PausedConsumerRetainsReservationUntilRelease() => Paused().GetAwaiter().GetResult();
    private static async Task Paused()
    {
        var root = WorldQueryTests.CreateWorld();
        try
        {
            var originals = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Hash256.Compute(File.ReadAllBytes(path)));
            string[]? baseline = null;
            foreach (var capacity in new[] { 2_097_152L, 16_777_216L })
            {
                var inventory = new ObservedInventory(new WorldInventory(root, World));
                var extraction = new WorldTextExtraction(inventory, new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)));
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var stored = new List<TextSourceExtraction>();
                inventory.Armed = true;
                var running = new WorldScanCoordinator(extraction, new(8, 16, capacity)).RunAsync(async (batch, token) =>
                {
                    if (batch.RelativePath.EndsWith(".mcfunction", StringComparison.Ordinal))
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(token);
                    }
                    stored.AddRange(batch.Sources);
                });
                ScanCompletion completion;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    if (capacity == 2_097_152)
                    {
                        Require(Volatile.Read(ref inventory.FunctionReads) == 1, "first function owns the only fitting reservation while its store is paused; reads=" + inventory.FunctionReads);
                        await Task.Delay(150);
                        Require(Volatile.Read(ref inventory.FunctionReads) == 1 && !running.IsCompleted,
                            "paused consumer retains its buffer lease and prevents another function read");
                    }
                    else
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (Volatile.Read(ref inventory.FunctionReads) < 3 && DateTime.UtcNow < deadline) await Task.Delay(10);
                        Require(Volatile.Read(ref inventory.FunctionReads) == 3 && !running.IsCompleted,
                            "same paused consumer permits all three function reads when the independent buffer budget fits them");
                    }
                }
                finally { release.TrySetResult(); completion = await running; }
                Require(completion.Finished && completion.UnfinishedSources.IsEmpty && completion.Telemetry.PeakReservedBytes <= capacity
                    && completion.Telemetry.ObservedQueuePeak <= 16, "release drains the exact bounded pipeline without lost work or leases");
                var rows = stored.Select(row => row.Source.Location.Artifact.RelativePath + "|" + row.Outcome + "|"
                    + string.Join(',', row.Text?.Occurrences.Select(item => SourceIdentity.Occurrence(item.Address).Digest.Hex + "=" + item.Text) ?? []))
                    .Order(StringComparer.Ordinal).ToArray();
                Require(baseline is null || baseline.SequenceEqual(rows), "buffer pressure preserves the complete source outcomes, occurrence identities and values");
                baseline = rows;
                Require(originals.Count == Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length
                    && originals.All(pair => Hash256.Compute(File.ReadAllBytes(pair.Key)) == pair.Value), "paused and resumed scans preserve every original byte");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
