using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.WorldQueryTests;

namespace NBTExplorerCLI.Verification;

internal static class CacheFreshnessReviewTests
{
    public static void BadPackMetadataStillBindsRawBytes() => Pack().GetAwaiter().GetResult();
    private static async Task Pack()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "datapacks/p/pack.mcmeta", [1, 2, 3]);
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); Put(root, "datapacks/p/pack.mcmeta", [4, 5, 6]);
            Require(service.Inspect(request).State == CacheState.Dirty, "known bad metadata bytes change the verified input identity even while schema parsing remains failed");
            var changed = await service.QueryAsync(request);
            Require(changed.Snapshot!.Manifest.SnapshotId != first.Snapshot!.Manifest.SnapshotId && !changed.Snapshot.Manifest.CoverageComplete
                && changed.Snapshot.ReadSources().Where(source => source.Source.Location.Role != WorldSourceRole.PackRoot).All(source => source.Source.ContentHash is not null), "bad metadata and its readable member sources all retain raw hashes separately from failed outcomes; synthetic directory failures are not raw files");
            var scoped = request with { Scan = new(new([], [], [], [Function(0)])) }; var before = await service.QueryAsync(scoped);
            Put(root, "datapacks/p/pack.mcmeta", [7, 8, 9]);
            Require(service.Inspect(scoped).State == CacheState.Dirty && (await service.QueryAsync(scoped)).Snapshot!.Manifest.SnapshotId != before.Snapshot!.Manifest.SnapshotId,
                "bad metadata outside an exact artifact scope remains a hashed dependency");
            Require(service.Inspect(scoped).State == CacheState.Fresh, "unchanged readable failed metadata has verified freshness independently of semantic coverage");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void UnknownWorldMetadataHashesFallbackSources() => Metadata().GetAwaiter().GetResult();
    private static async Task Metadata()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(9999, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); Put(root, Function(0), [1, 2, 3]);
            Require(service.Inspect(request).State == CacheState.Dirty, "unsupported metadata does not hide raw content changes in unprocessed fallback sources");
            var changed = await service.QueryAsync(request);
            Require(changed.Snapshot!.Manifest.SnapshotId != first.Snapshot!.Manifest.SnapshotId && !changed.Snapshot.Manifest.CoverageComplete
                && changed.Snapshot.Manifest.InputSources.All(source => source.ContentHash is not null), "readable fallback sources bind hashes while outcomes remain unsupported");
            Require(service.Inspect(request).State == CacheState.Fresh, "unchanged fallback hashes can be verified without claiming extraction support");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private sealed class Unreadable(IWorldInventory actual) : IWorldInventory
    {
        public Guid WorldId => actual.WorldId;
        public WorldInventorySnapshot Enumerate(int maximum = 100_000) => actual.Enumerate(maximum);
        public byte[] Read(ArtifactAddress artifact) => artifact.RelativePath == "notes.txt" ? throw new IOException("Unreadable probe") : actual.Read(artifact);
    }
    public static void UnverifiedSourcesCannotBeFresh() => Unverified().GetAwaiter().GetResult();
    private static async Task Unverified()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "notes.txt", [1]);
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new Unreadable(new WorldInventory(root, World)), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request);
            Require(!first.Snapshot!.Manifest.CoverageComplete && first.Snapshot.Manifest.InputSources.Any(source => source.ContentHash is null), "IO failure remains explicit and never invents a raw hash");
            Require(service.Inspect(request) == new CacheInspection(CacheState.Dirty, "UnverifiedSources"), "sources without verified content identity cannot be accepted as Fresh");
            Require((await service.QueryAsync(request)).Telemetry.ManifestHits == 0, "unverified snapshot cannot enter the warm manifest-hit branch");
            var readable = new WorldQueryService(new WorldInventory(root, World), store); var repaired = await readable.QueryAsync(request);
            Require(repaired.Snapshot!.Manifest.CoverageComplete && readable.Inspect(request).State == CacheState.Fresh, "restored readability permits verified current content, with the previous failure remaining historical");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
