using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class ScanCoordinatorTests
{
    private static string WorldWithFunctions(int count)
    {
        var root = ScratchDirectory.CreateChild(); Level(root, false);
        Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
        for (var index = 0; index < count; index++) Put(root, "datapacks/p/data/test/function/f" + index + ".mcfunction", Encoding.UTF8.GetBytes("tellraw @a[name=Guard] \"Text" + index + "\"\n"));
        return root;
    }
    public static void WorkerParityAndBounds() => WorkerParity().GetAwaiter().GetResult();
    private static async Task WorkerParity()
    {
        var root = WorldWithFunctions(12);
        try
        {
            var inventory = new WorldInventory(root, World);
            var before = inventory.Enumerate().Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))), StringComparer.Ordinal);
            string[]? baseline = null;
            foreach (var workers in new[] { 1, 2, 8 })
            {
                var extraction = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)));
                var expected = extraction.ReadSources().Select(Row).Order(StringComparer.Ordinal).ToArray();
                var actual = new List<string>(); var callbackActive = 0;
                var options = new ScanExecutionOptions(workers, 2, 4_194_304);
                var result = await new WorldScanCoordinator(extraction, options).RunAsync(async (batch, token) =>
                {
                    Require(Interlocked.Increment(ref callbackActive) == 1, "single store coordinator owns shared output");
                    await Task.Delay(2, token); actual.AddRange(batch.Sources.Select(Row)); Interlocked.Decrement(ref callbackActive);
                });
                var sorted = actual.Order(StringComparer.Ordinal).ToArray();
                Require(result.Finished && result.UnfinishedSources.IsEmpty && sorted.SequenceEqual(expected), "bounded worker execution matches real sequential codec/command source semantics");
                Require(baseline is null || sorted.SequenceEqual(baseline), "canonical source/text/reference results equal at workers1/default/max"); baseline = sorted;
                Require(result.Telemetry.Workers == workers && result.Telemetry.ObservedQueuePeak <= options.QueuedArtifacts && result.Telemetry.PeakReservedBytes <= options.InFlightBytes
                    && result.Telemetry.PeakActiveWorkers <= workers && result.Telemetry.StoredSources == expected.Length && result.CompletedArtifacts.Length == result.Telemetry.StoredArtifacts,
                    "queue, memory reservation, worker and source accounting agree with configured bounds");
            }
            var after = inventory.Enumerate().Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))), StringComparer.Ordinal);
            Require(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), "all worker modes preserve the original world file set and raw contents");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void CapacityFailureAndStoreError() => Capacity().GetAwaiter().GetResult();
    private static async Task Capacity()
    {
        var root = WorldWithFunctions(4);
        try
        {
            var extraction = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)));
            var failures = new List<TextSourceExtraction>();
            var result = await new WorldScanCoordinator(extraction, new(2, 1, 512)).RunAsync((batch, _) => { failures.AddRange(batch.Sources); return ValueTask.CompletedTask; });
            Require(result.Finished && result.UnfinishedSources.IsEmpty && failures.Count == extraction.Discovery.Sources.Length
                && failures.All(source => source.Outcome == SourceOutcome.Failed && source.DiagnosticCode == "LimitExceeded" && source.Text is null)
                && result.Telemetry.PeakReservedBytes == 0, "input larger than reservation capacity returns exact failure outcomes without waiting forever or reporting empty success");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var preCancelled = await new WorldScanCoordinator(extraction).RunAsync((_, _) => throw new InvalidOperationException("Store must not run"), cancelled.Token);
            Require(!preCancelled.Finished && preCancelled.CompletedArtifacts.IsEmpty && preCancelled.UnfinishedSources.Length == extraction.Discovery.Sources.Length, "pre-cancelled operation still accounts for its whole discovered catalog");
            var coordinator = new WorldScanCoordinator(extraction, new(2, 1, 4_194_304));
            try { await coordinator.RunAsync((_, _) => throw new IOException("Injected store failure")); throw new InvalidOperationException("Expected store failure"); }
            catch (IOException error) when (error.Message == "Injected store failure") { }
            var stored = 0;
            Require((await coordinator.RunAsync((batch, _) => { stored += batch.Sources.Length; return ValueTask.CompletedTask; })).Finished && stored == extraction.Discovery.Sources.Length,
                "store failure drains workers and leases; retry can complete without stale resource ownership");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class ParallelInventory(IWorldInventory actual) : IWorldInventory, IDisposable
    {
        internal bool Armed;
        internal int Peak;
        private int active;
        private readonly ManualResetEventSlim both = new();
        public Guid WorldId => actual.WorldId;
        public WorldInventorySnapshot Enumerate(int maximum = 100_000) => actual.Enumerate(maximum);
        public byte[] Read(ArtifactAddress artifact)
        {
            if (!Armed || !artifact.RelativePath.EndsWith(".mcfunction", StringComparison.Ordinal)) return actual.Read(artifact);
            var count = Interlocked.Increment(ref active); int before;
            do { before = Peak; if (before >= count) break; } while (Interlocked.CompareExchange(ref Peak, count, before) != before);
            if (count >= 2) both.Set();
            try { Require(both.Wait(TimeSpan.FromSeconds(5)), "two configured workers must reach independent input reads concurrently"); return actual.Read(artifact); }
            finally { Interlocked.Decrement(ref active); }
        }
        public void Dispose() => both.Dispose();
    }
    public static void IndependentWorkersReallyOverlap() => Parallel().GetAwaiter().GetResult();
    private static async Task Parallel()
    {
        var root = WorldWithFunctions(4);
        try
        {
            using var inventory = new ParallelInventory(new WorldInventory(root, World));
            var extraction = new WorldTextExtraction(inventory, new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576))); inventory.Armed = true;
            var result = await new WorldScanCoordinator(extraction, new(2, 1, 4_194_304)).RunAsync((_, _) => ValueTask.CompletedTask);
            Require(result.Finished && inventory.Peak == 2 && result.Telemetry.PeakActiveWorkers == 2, "worker setting changes actual execution concurrency while retaining single output ownership");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ArtifactOutputRetentionIsBounded() => Retention().GetAwaiter().GetResult();
    private static async Task Retention()
    {
        var root = WorldWithFunctions(0);
        try
        {
            Put(root, "arbitrary.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/test/lang/a.json", "{\"a\":\"A\"}"), ("assets/test/lang/b.json", "{\"b\":\"B\"}")));
            var extraction = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All, new(MaxDecodedBytes: 1000)));
            Require(extraction.ReadSources().Where(source => source.Source.Location.Artifact.RelativePath == "arbitrary.zip").All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant),
                "each entry separately fits its codec and text budget before the artifact retention boundary");
            var batches = new List<ArtifactScanBatch>();
            var result = await new WorldScanCoordinator(extraction, new(2, 1, 4096)).RunAsync((batch, _) => { batches.Add(batch); return ValueTask.CompletedTask; });
            var archive = batches.Single(batch => batch.RelativePath == "arbitrary.zip");
            Require(result.Finished && archive.Sources.Length == 4 && archive.Sources.All(source => source.Outcome == SourceOutcome.Failed && source.DiagnosticCode == "LimitExceeded" && source.Text is null),
                "aggregate artifact output has an explicit bound and exact failure sources instead of an unbounded retained batch or partial complete result");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void CancellationRetainsSourceAccounting() => Cancel().GetAwaiter().GetResult();
    private static async Task Cancel()
    {
        var root = WorldWithFunctions(24);
        try
        {
            var extraction = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)));
            using var cancellation = new CancellationTokenSource(); var stored = new List<TextSourceExtraction>();
            var result = await new WorldScanCoordinator(extraction, new(2, 1, 4_194_304)).RunAsync((batch, _) =>
            {
                stored.AddRange(batch.Sources); cancellation.Cancel(); return ValueTask.CompletedTask;
            }, cancellation.Token);
            Require(!result.Finished && stored.Count > 0 && result.CompletedArtifacts.Length == 1 && result.UnfinishedSources.Length > 0 && result.UnfinishedSources.All(source => source.Outcome == SourceOutcome.Cancelled),
                "cancellation retains completed and explicit unprocessed/cancelled source outcomes");
            Require(stored.Count + result.UnfinishedSources.Length == extraction.Discovery.Sources.Length && stored.Select(source => Key(source)).Intersect(result.UnfinishedSources.Select(Key), StringComparer.Ordinal).Count() == 0,
                "cancelled and stored sources form an exact nonoverlapping catalog partition");
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Key(TextSourceExtraction source) => source.Source.Location.Artifact.RelativePath + "|" + source.Source.Location.Artifact.ContainerEntry + "|" + source.Source.Chunk;
    private static string Row(TextSourceExtraction source) => Key(source) + "|" + source.Outcome + "|" + source.DiagnosticCode + "|"
        + string.Join(',', source.Text?.Occurrences.Select(text => SourceIdentity.Occurrence(text.Address).Digest.Hex + ":" + text.Text) ?? []) + "|"
        + string.Join(',', source.Commands?.References.Select(reference => SourceIdentity.Occurrence(reference.Address).Digest.Hex + ":" + reference.Name) ?? []);
}
