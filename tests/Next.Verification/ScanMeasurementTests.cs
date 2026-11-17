using System.Text;
using Next.Application;
using Next.Benchmarks;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class ScanMeasurementTests
{
    public static void FullWarmAndWorkersRetainSemanticsAndActualBoundaryCounters()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World); var cache = new FileSystemScanCacheStore(work);
            var bytes = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            ScanSample Sample(int workers, bool full) => ScanMeasurement.RunAsync(inventory, cache, new(SnapshotScope.All), new(workers), full).GetAwaiter().GetResult();
            var full = Sample(1, true); var warm = Sample(1, false); var parallel = Sample(8, true);
            // All scope includes the pack's "Data" description and three tellraw literals.
            Require(full.CoverageComplete && full.Counts is { Sources: 5, Occurrences: 4, CommandReferences: 3, Failed: 0, Unsupported: 0, Cancelled: 0 }, "measurement retains complete real command source/occurrence/reference counts: " + full.Counts + ", coverage=" + full.CoverageComplete);
            Require(full.SemanticHash == warm.SemanticHash && full.SemanticHash == parallel.SemanticHash
                && full.OccurrenceHash == warm.OccurrenceHash && full.OccurrenceHash == parallel.OccurrenceHash, "full/warm and worker changes preserve every semantic record and occurrence identity");
            Require(full.Query.ScannedArtifacts == 5 && warm.Query.ScannedArtifacts == 0 && warm.Query.ManifestHits == 1 && warm.Before.State == CacheState.Fresh, "measurement distinguishes verified warm source checks from actual parsing");
            foreach (var sample in new[] { full, warm, parallel })
                Require(sample.Cost.SourceReads > 0 && sample.Cost.SourceReadBytes >= bytes && sample.Cost.UniqueSourceBytes == bytes
                    && sample.Cost.CacheReadBytes > 0 && sample.Cost.ManagedAllocatedBytes > 0 && sample.Cost.PeakWorkingSetBytes > 0
                    && sample.Cost.WallMilliseconds > 0 && sample.Cost.CpuMilliseconds >= 0, "resource counters describe actual boundaries and never confuse cumulative allocation with resident memory");
            Require(full.Cost.CacheSubmittedBytes > 0 && warm.Cost.CacheSubmittedBytes == 0 && parallel.Execution is { Workers: 8, ObservedQueuePeak: <= 16 }, "submitted cache payloads and worker telemetry remain separate from semantic equality");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    public static void ChangedAndBrokenSourcesCannotBecomeFasterCompleteResults()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World); var cache = new FileSystemScanCacheStore(work);
            ScanSample Sample(bool full) => ScanMeasurement.RunAsync(inventory, cache, new(SnapshotScope.All), new(1), full).GetAwaiter().GetResult();
            var first = Sample(true);
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("tellraw @a[name=Guard] \"Changed\"\n"));
            var changed = Sample(false); var rebuilt = Sample(true);
            Require(changed.Before.State == CacheState.Dirty && changed.Query.ScannedArtifacts == 1 && changed.Query.ReusedArtifacts == 4
                && changed.SemanticHash != first.SemanticHash && changed.SemanticHash == rebuilt.SemanticHash, "incremental measurement preserves source hash validation and matches full rebuild truth");
            Put(root, "data/broken.dat", [1, 2, 3]);
            var broken = Sample(true);
            Require(!broken.CoverageComplete && broken.Counts is { Sources: 6, Occurrences: 4, Failed: 1 }
                && broken.SemanticHash != rebuilt.SemanticHash, "malformed sources remain counted and incomplete, not silently skipped from a timing sample: " + broken.Counts + ", coverage=" + broken.CoverageComplete);
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    public static void InvalidLimitsAndPrecancelRefuseBeforeAnySourceWork()
    {
        var inventory = new RefuseInventory(); var cache = new RefuseCache();
        var invalid = false; var cancelled = false;
        try { ScanMeasurement.RunAsync(inventory, cache, new(SnapshotScope.All), new(9), true).GetAwaiter().GetResult(); }
        catch (ArgumentOutOfRangeException) { invalid = true; }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { ScanMeasurement.RunAsync(inventory, cache, new(SnapshotScope.All), new(1), true, stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(invalid && cancelled && inventory.Calls == 0 && cache.Calls == 0, "invalid options and precancel cannot read sources or publish cache state");
    }

    public static void CatalogDiagnosticsRemainVisibleWhenEveryReturnedSourceIsGood()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new DiagnosedInventory(new WorldInventory(root, World));
            var sample = ScanMeasurement.RunAsync(inventory, new FileSystemScanCacheStore(work), new(SnapshotScope.All), new(1), true).GetAwaiter().GetResult();
            Require(!sample.CoverageComplete && sample.Counts.Failed == 0 && sample.Diagnostics.GetValueOrDefault("catalog:RootEnumerationFailed") == 1,
                "a root catalog failure must stay explicit even when every returned source is readable");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    private sealed class DiagnosedInventory(IWorldInventory inner) : IWorldInventory
    {
        public Guid WorldId => inner.WorldId;
        public Hash256? AccessBinding => inner.AccessBinding;
        public WorldInventorySnapshot Enumerate(int maxEntries)
        {
            var rows = inner.Enumerate(maxEntries); return new(rows.Entries, "RootEnumerationFailed", rows.DirectoryPaths);
        }
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
    }

    private sealed class RefuseInventory : IWorldInventory
    {
        public Guid WorldId => World;
        internal int Calls;
        public WorldInventorySnapshot Enumerate(int maxEntries) { Calls++; throw new InvalidOperationException("Unexpected inventory work"); }
        public byte[] Read(ArtifactAddress artifact) { Calls++; throw new InvalidOperationException("Unexpected source read"); }
    }
    private sealed class RefuseCache : IScanCacheStore
    {
        internal int Calls;
        public byte[]? ReadManifest() { Calls++; throw new InvalidOperationException("Unexpected manifest read"); }
        public byte[]? ReadShard(Hash256 hash) { Calls++; throw new InvalidOperationException("Unexpected shard read"); }
        public void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes) { Calls++; throw new InvalidOperationException("Unexpected shard write"); }
        public void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expectedManifest) { Calls++; throw new InvalidOperationException("Unexpected publish"); }
    }
}
