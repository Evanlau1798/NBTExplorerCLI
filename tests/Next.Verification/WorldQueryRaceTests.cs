using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldQueryTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldQueryRaceTests
{
    private sealed class Hooks(IScanCacheStore actual) : IScanCacheStore
    {
        internal Action? OnRead, OnWrite;
        public byte[]? ReadManifest() => actual.ReadManifest();
        public byte[]? ReadShard(Hash256 hash) { var bytes = actual.ReadShard(hash); Interlocked.Exchange(ref OnRead, null)?.Invoke(); return bytes; }
        public void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes) { actual.WriteShard(hash, bytes); Interlocked.Exchange(ref OnWrite, null)?.Invoke(); }
        public void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expected) => actual.PublishManifest(bytes, expected);
    }
    private static void Change(string root, string text) => Put(root, Function(0), Encoding.UTF8.GetBytes("tellraw @s \"" + text + "\""));
    public static void WarmSourcesAreRevalidated() => Warm().GetAwaiter().GetResult();
    private static async Task Warm()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var disk = new FileSystemScanCacheStore(cache); var hooks = new Hooks(disk);
            var service = new WorldQueryService(new WorldInventory(root, World), hooks); var request = new WorldQueryRequest(new(SnapshotScope.All));
            await service.QueryAsync(request); var head = disk.ReadManifest()!;
            hooks.OnRead = () => Change(root, "Raced");
            Require(service.Inspect(request).State == CacheState.Dirty, "inspect revalidates world inputs after reading cache shards before returning Fresh");
            await service.QueryAsync(request); head = disk.ReadManifest()!;
            hooks.OnRead = () => Change(root, "RacedAgain");
            try { await service.QueryAsync(request); throw new InvalidOperationException("Warm query returned stale Fresh"); }
            catch (ArtifactConflictException) { }
            Require(disk.ReadManifest()!.SequenceEqual(head), "warm source conflict preserves the previously published manifest");
            Require((await service.QueryAsync(request)).Snapshot!.ReadSources().SelectMany(source => source.Text?.Occurrences ?? []).Any(text => text.Text == "RacedAgain"), "explicit retry observes the current source");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void PublicationFailuresKeepPreviousHead() => Publication().GetAwaiter().GetResult();
    private static async Task Publication()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var disk = new FileSystemScanCacheStore(cache); var hooks = new Hooks(disk);
            var service = new WorldQueryService(new WorldInventory(root, World), hooks); var request = new WorldQueryRequest(new(SnapshotScope.All));
            await service.QueryAsync(request); var head = disk.ReadManifest()!;
            Change(root, "Cancel"); using var cancelled = new CancellationTokenSource(); hooks.OnWrite = cancelled.Cancel;
            var interrupted = await service.QueryAsync(request, cancelled.Token);
            Require(interrupted.Snapshot is null && !interrupted.Telemetry.Published && interrupted.Scan is { Finished: false }
                && disk.ReadManifest()!.SequenceEqual(head), "cancellation after a verified shard retains the old head and exposes no complete snapshot");
            hooks.OnWrite = () => throw new IOException("Injected cache store failure");
            try { await service.QueryAsync(request); throw new InvalidOperationException("Expected store failure"); }
            catch (IOException error) when (error.Message == "Injected cache store failure") { }
            Require(disk.ReadManifest()!.SequenceEqual(head), "failed shard publication cannot advance the manifest");
            hooks.OnWrite = () => Change(root, "PublicationRace");
            try { await service.QueryAsync(request); throw new InvalidOperationException("Expected source race"); }
            catch (ArtifactConflictException) { }
            Require(disk.ReadManifest()!.SequenceEqual(head), "source mutation during scan is rejected before head publication");
            var repaired = await service.QueryAsync(request);
            Require(repaired.Telemetry.Published && repaired.Snapshot!.ReadSources().SelectMany(source => source.Text?.Occurrences ?? []).Any(text => text.Text == "PublicationRace"), "retry after cancel, store failure and source race releases resources and publishes current results");
            Put(root, Function(2), Encoding.UTF8.GetBytes("tellraw @s \"IncrementalCancel\""));
            using var reuseCancellation = new CancellationTokenSource(); var reads = 0; var inspectedShards = repaired.Snapshot!.Manifest.Artifacts.Length;
            void DuringReuse() { if (++reads > inspectedShards) reuseCancellation.Cancel(); else hooks.OnRead = DuringReuse; }
            hooks.OnRead = DuringReuse;
            var reuseCancelled = await service.QueryAsync(request with { Execution = new(1, 1) }, reuseCancellation.Token);
            Require(!reuseCancelled.Telemetry.Published && reuseCancelled.Telemetry.ScannedArtifacts + reuseCancelled.Telemetry.ReusedArtifacts == reuseCancelled.Scan!.Telemetry.StoredArtifacts,
                "cancelled reuse telemetry counts stored work, not worker results discarded during cancellation");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private sealed class CancelInventory(IWorldInventory actual, CancellationTokenSource cancellation) : IWorldInventory
    {
        internal int Reads;
        public Guid WorldId => actual.WorldId;
        public WorldInventorySnapshot Enumerate(int maximum = 100_000) => actual.Enumerate(maximum);
        public byte[] Read(ArtifactAddress artifact) { Reads++; var bytes = actual.Read(artifact); cancellation.Cancel(); return bytes; }
    }
    public static void DiscoveryCancellationStopsNextRead() => Discovery().GetAwaiter().GetResult();
    private static async Task Discovery()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            using var cancellation = new CancellationTokenSource(); var inventory = new CancelInventory(new WorldInventory(root, World), cancellation);
            var disk = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(inventory, disk);
            try { await service.QueryAsync(new(new(SnapshotScope.All)), cancellation.Token); throw new InvalidOperationException("Expected cancellation"); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Require(inventory.Reads == 1 && disk.ReadManifest() is null, "discovery cancellation is checked between bounded input reads and never publishes a cache");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
