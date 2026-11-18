using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.WorldQueryTests;

namespace NBTExplorerCLI.Verification;

internal static class ScanCacheReviewTests
{
    public static void CorruptInputsStillHaveRawRevisions() => Corrupt().GetAwaiter().GetResult();
    private static async Task Corrupt()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var path = "region/r.0.0.mca"; Put(root, path, [1, 2, 3]);
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); Put(root, path, [4, 5, 6]);
            Require(service.Inspect(request) == new CacheInspection(CacheState.Dirty, "ContentChanged"), "readable malformed bytes retain raw content identity independently of the parse failure");
            var second = await service.QueryAsync(request);
            Require(first.Snapshot!.Manifest.SnapshotId != second.Snapshot!.Manifest.SnapshotId && second.Telemetry.ScannedArtifacts == 1
                && second.Snapshot.Manifest.InputSources.Single(source => source.Artifact.RelativePath == path).ContentHash == Hash256.Compute(new byte[] { 4, 5, 6 })
                && second.Snapshot.ReadSources().Single(source => source.Source.Location.Artifact.RelativePath == path) is { Outcome: SourceOutcome.Failed, DiagnosticCode: "InvalidRegion" },
                "changed bad Region reparses only its shard, has a new snapshot, and keeps its precise failure");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static void Rewrite(FileSystemScanCacheStore store, string path, Func<ArtifactCacheShard, ArtifactCacheShard> change)
    {
        var head = store.ReadManifest()!; var manifest = ScanCacheJson.ReadManifest(Encoding.UTF8.GetString(head)); var old = manifest.Artifacts.Single(item => item.RelativePath == path);
        var shard = change(ScanCacheJson.ReadShard(Encoding.UTF8.GetString(store.ReadShard(old.ContentHash)!)));
        var bytes = Encoding.UTF8.GetBytes(ScanCacheJson.WriteShard(shard)); var hash = Hash256.Compute(bytes); store.WriteShard(hash, bytes);
        var descriptor = old with { ContentHash = hash, TextCount = shard.Sources.Sum(source => source.Text?.Occurrences.Length ?? 0), ReferenceCount = shard.Sources.Sum(source => source.Commands?.References.Length ?? 0), CoverageComplete = shard.Sources.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant) };
        var artifacts = manifest.Artifacts.Select(item => item.RelativePath == path ? descriptor : item).ToImmutableArray();
        var changed = manifest with { Artifacts = artifacts, CoverageComplete = manifest.CatalogDiagnosticCode is null && artifacts.All(item => item.CoverageComplete) && manifest.InputSources.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant) };
        store.PublishManifest(Encoding.UTF8.GetBytes(ScanCacheJson.WriteManifest(changed)), Hash256.Compute(head));
    }
    public static void CatalogOutcomesCannotBeOverridden() => Outcomes().GetAwaiter().GetResult();
    private static async Task Outcomes()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var path = "region/r.0.0.mca"; var bytes = Region(); bytes[8196] = 9; Put(root, path, bytes);
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request);
            Require(!first.Snapshot!.Manifest.CoverageComplete && first.Snapshot.ReadSources().Any(source => source.Source.Chunk is not null && source.Outcome == SourceOutcome.Unsupported), "unknown compression produces a catalog-level unsupported positive control");
            Rewrite(store, path, shard => shard with { Sources = shard.Sources.Select(source => source.Chunk is not null ? source with { Outcome = SourceOutcome.Irrelevant, DiagnosticCode = null } : source).ToImmutableArray() });
            Require(service.Inspect(request).State == CacheState.Corrupt, "cache cannot replace a catalog-level unsupported outcome with irrelevant or complete success");
            var repaired = await service.QueryAsync(request);
            Require(repaired.Telemetry.ScannedArtifacts == 1 && !repaired.Snapshot!.Manifest.CoverageComplete && repaired.Snapshot.ReadSources().Any(source => source.Source.Chunk is not null && source.Outcome == SourceOutcome.Unsupported), "outcome binding repair restores incomplete semantics and reuses good artifacts");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void CommandsOnlyDimensionBinding() => Commands().GetAwaiter().GetResult();
    private static async Task Commands()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            await service.QueryAsync(request);
            Rewrite(store, Function(0), shard => shard with { Sources = shard.Sources.Select(source => source with { Outcome = SourceOutcome.Unsupported, DiagnosticCode = "UnsupportedProbe", Text = null,
                Commands = source.Commands! with { Text = source.Commands.Text with { Occurrences = source.Commands.Text.Occurrences.Select(item => item with { Address = OtherDimension(item.Address) }).ToImmutableArray() },
                    References = source.Commands.References.Select(item => item with { Address = OtherDimension(item.Address) }).ToImmutableArray() } }).ToImmutableArray() });
            Require(service.Inspect(request).State == CacheState.Corrupt, "command occurrence and reference dimensions are validated even without the merged Text result");
            Require((await service.QueryAsync(request)).Snapshot!.ReadSources().Where(source => source.Commands is not null).SelectMany(source => source.Commands!.References).All(reference => reference.Address.Dimension is null), "repaired command results use only current catalog dimensions");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static SourceAddress OtherDimension(SourceAddress address) => new(address.Artifact, address.NodePath, address.SemanticRole, "minecraft:the_nether", address.Chunk, address.ComponentPath, address.Syntax, address.JsonTarget);
    public static void TotalQueueCapacityIsBounded() => Queue().GetAwaiter().GetResult();
    private static async Task Queue()
    {
        var root = CreateWorld();
        try
        {
            for (var index = 3; index < 60; index++) Put(root, Function(index), Encoding.UTF8.GetBytes("tellraw @s \"Queued\""));
            var extraction = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = new WorldScanCoordinator(extraction, new(8, 16)).RunAsync(async (_, _) => { entered.TrySetResult(); await release.Task; });
            var queued = 0; ScanCompletion completion;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (true)
                {
                    var counts = ChannelCounts(running); queued = counts.Sum();
                    if (counts.All(count => count == 16) || DateTime.UtcNow >= deadline) break;
                    await Task.Delay(10);
                }
            }
            finally { release.TrySetResult(); completion = await running; }
            Require(completion.Finished && queued == 16 && completion.Telemetry.ObservedQueuePeak == 16, "slow store saturates exactly sixteen total queued artifacts across all pipeline channels, not sixteen per stage");
        }
        finally { Directory.Delete(root, true); }
    }
    private static int[] ChannelCounts(Task task)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var state = task.GetType().GetField("StateMachine", flags)!.GetValue(task)!;
        var closure = state.GetType().GetField("<>8__1", flags)!.GetValue(state)!;
        return closure.GetType().GetFields(flags).Where(field => field.Name is "work" or "results").Select(field =>
        {
            var channel = field.GetValue(closure)!; var reader = field.FieldType.GetProperty("Reader")!.GetValue(channel)!;
            return (int)reader.GetType().GetProperty("Count")!.GetValue(reader)!;
        }).ToArray();
    }
    public static void WriterMarkerAccountingUsesOneCatalog() => Marker().GetAwaiter().GetResult();
    private static async Task Marker()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            using (var candidate = new ArtifactStorage(root, World).Stage(new(World, "probe.bin", ArtifactKind.Text), ArtifactImage.Absent, new byte[] { 1 })) { }
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var full = await service.QueryAsync(request);
            Require(full.Scan!.Telemetry.StoredArtifacts == full.Telemetry.ScannedArtifacts + full.Telemetry.ReusedArtifacts
                && full.Scan.Telemetry.StoredSources == full.Snapshot!.ReadSources().Count() && !full.Scan.CompletedArtifacts.Contains(".nbtcli-writer.lock"), "coordinator, query and manifest use the same filtered catalog and conserved artifact/source accounting");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var extraction = new WorldTextExtraction(new WorldInventory(root, World), request.Scan);
            var result = await new WorldScanCoordinator(extraction).RunAsync((_, _) => ValueTask.CompletedTask, cancelled.Token);
            Require(result.UnfinishedSources.All(source => source.Source.DiagnosticCode != "InternalWriterLease") && result.UnfinishedSources.Length == full.Snapshot!.ReadSources().Count(), "cancellation partition excludes internal writer markers from the same semantic catalog");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
