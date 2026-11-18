using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldQueryTests
{
    internal static WorldQuerySnapshot Query(string root, string cache) => new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)).QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
    internal static void BossWorld(string root, string text = "Hello", string? componentJson = null)
    {
        var level = WorldMetadataTests.Level(3953, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)level.Root).Entries.Single().Value;
        var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString(componentJson ?? JsonSerializer.Serialize(new { text })))]))]);
        Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
    }
    internal static string CreateWorld()
    {
        var root = ScratchDirectory.CreateChild(); Level(root, false);
        Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(Format48Pack));
        for (var index = 0; index < 3; index++) Put(root, Function(index), Encoding.UTF8.GetBytes("tellraw @a[name=Guard] \"Text" + index + "\"\n"));
        return root;
    }
    internal static string Function(int index) => "datapacks/p/data/test/function/f" + index + ".mcfunction";
    internal static string[] Rows(WorldQuerySnapshot snapshot) => snapshot.ReadSources().Select(row => row.Source.Location.Artifact.RelativePath + "|" + row.Source.Location.Artifact.ContainerEntry + "|" + row.Source.Chunk
        + "|" + row.Outcome + "|" + row.DiagnosticCode + "|" + string.Join(',', row.Text?.Occurrences.Select(text => SourceIdentity.Occurrence(text.Address).Digest.Hex + ":" + text.Text) ?? [])
        + "|" + string.Join(',', row.Commands?.References.Select(reference => reference.Name + ":" + reference.Negated) ?? [])).Order(StringComparer.Ordinal).ToArray();
    public static void SharedConsumersAndWarmQuery() => Shared().GetAwaiter().GetResult();
    private static async Task Shared()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache));
            var request = new WorldQueryRequest(new(SnapshotScope.All));
            Require(service.Inspect(request).State == CacheState.Missing, "missing cache is explicit before any query");
            var full = await service.QueryAsync(request with { ForceFull = true });
            Require(full.Snapshot is not null && full.Snapshot.Manifest.CoverageComplete && full.Telemetry.ScannedArtifacts > 0 && full.Telemetry.ReusedArtifacts == 0 && full.Telemetry.Published,
                "full query stores real codec/command results and publishes a complete source snapshot");
            var expected = Rows(full.Snapshot!); var id = full.Snapshot!.Manifest.SnapshotId;
            foreach (var purpose in Enum.GetValues<WorldQueryPurpose>())
            {
                var warm = await service.QueryAsync(request with { Purpose = purpose });
                Require(warm.Before.State == CacheState.Fresh && warm.Snapshot!.Manifest.SnapshotId == id && Rows(warm.Snapshot).SequenceEqual(expected)
                    && warm.Telemetry.ManifestHits == 1 && warm.Telemetry.ScannedArtifacts == 0 && warm.Telemetry.ReusedArtifacts > 0 && !warm.Telemetry.Published,
                    "scan/extract/session/convert use the same verified cache contract, with semantic results separate from warm telemetry");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void SameSizeMtimeIncrementalEqualsFull() => Incremental().GetAwaiter().GetResult();
    private static async Task Incremental()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); var path = Path.Combine(root, Function(0)); var time = File.GetLastWriteTimeUtc(path); var before = File.ReadAllBytes(path);
            var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(before).Replace("Text0", "Other", StringComparison.Ordinal));
            Require(changed.Length == before.Length, "same-size content mutation is a discriminating input"); File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, time);
            Require(service.Inspect(request).State == CacheState.Dirty, "hash-based freshness detects same size and mtime content change");
            var incremental = await service.QueryAsync(request);
            Require(incremental.Before.State == CacheState.Dirty && incremental.Telemetry.ScannedArtifacts == 1 && incremental.Telemetry.ReusedArtifacts > 0
                && incremental.Snapshot!.Manifest.SnapshotId != first.Snapshot!.Manifest.SnapshotId, "only changed artifact is parsed while immutable shards remain reusable");
            var full = await service.QueryAsync(request with { ForceFull = true });
            Require(incremental.Snapshot!.Manifest.SnapshotId == full.Snapshot!.Manifest.SnapshotId && Rows(incremental.Snapshot).SequenceEqual(Rows(full.Snapshot)),
                "incremental and clean full query have identical canonical semantic sources, slots and references");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
