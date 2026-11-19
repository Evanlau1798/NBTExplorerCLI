using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldQueryService
{
    private readonly IWorldInventory inventory;
    private readonly IScanCacheStore store;
    public WorldQueryService(IWorldInventory inventory, IScanCacheStore store)
    { ArgumentNullException.ThrowIfNull(inventory); ArgumentNullException.ThrowIfNull(store); this.inventory = inventory; this.store = store; }
    public CacheInspection Inspect(WorldQueryRequest request, CancellationToken cancellationToken = default)
    {
        var context = new WorldQueryContext(inventory, request, cancellationToken); var inspection = Existing(context, cancellationToken).Inspection;
        return inspection.State == CacheState.Fresh && !Unchanged(context, new(inventory, request, cancellationToken))
            ? new(CacheState.Dirty, "SourceChangedDuringValidation") : inspection;
    }
    public async Task<WorldQueryResult> QueryAsync(WorldQueryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var clock = Stopwatch.StartNew();
        var context = new WorldQueryContext(inventory, request, cancellationToken); var previous = Existing(context, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (previous.Inspection.State == CacheState.Fresh && !request.ForceFull)
        {
            if (!Unchanged(context, new(inventory, request, cancellationToken))) throw new ArtifactConflictException("Source snapshot changed during cache validation");
            return new(View(previous.Manifest!, context), previous.Inspection, null, new(1, 0, 0, 0, context.Groups.Count, 0, previous.Manifest!.Artifacts.Sum(artifact => artifact.SourceCount), clock.ElapsedMilliseconds, false));
        }
        var old = previous.Manifest?.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal) ?? [];
        var reused = new ConcurrentDictionary<string, CachedArtifact>(StringComparer.Ordinal); var descriptors = new List<CachedArtifact>();
        var parsedArtifacts = 0; var reusedArtifacts = 0; var scannedSources = 0; var reusedSources = 0;
        ArtifactScanBatch? Reuse(ImmutableArray<DiscoveredSource> group)
        {
            var path = group[0].Location.Artifact.RelativePath;
            if (request.ForceFull || previous.Manifest is null || !SameInterpretation(previous.Manifest.Versions, context.Versions)
                || !context.ParseKeys.TryGetValue(path, out var key) || !old.TryGetValue(path, out var descriptor) || descriptor.ParseKey != key) return null;
            try
            {
                var batch = CachedArtifactResults.Bind(CachedArtifactResults.Read(store, descriptor, inventory.WorldId), group); reused[path] = descriptor; return batch;
            }
            catch (Exception error) when (CacheFailure(error)) { return null; }
        }
        var completion = await new WorldScanCoordinator(context.Extraction, context.Execution).RunCachedAsync((batch, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!context.Groups.ContainsKey(batch.RelativePath)) return ValueTask.CompletedTask;
            if (batch.Sources.Any(source => source.DiagnosticCode is "SourceChangedDuringExtraction" or "PackMetadataChanged"
                || source.Source.Outcome == SourceOutcome.Success && source.DiagnosticCode == "SourceReadFailed")) throw new ArtifactConflictException("Source changed or became unavailable during extraction");
            if (reused.TryGetValue(batch.RelativePath, out var descriptor)) { descriptors.Add(descriptor); reusedSources += batch.Sources.Length; reusedArtifacts++; }
            else
            {
                descriptor = CachedArtifactResults.Write(store, batch, context.ParseKeys[batch.RelativePath], token);
                _ = CachedArtifactResults.Read(store, descriptor, inventory.WorldId);
                descriptors.Add(descriptor); parsedArtifacts++; scannedSources += batch.Sources.Length;
            }
            return ValueTask.CompletedTask;
        }, Reuse, cancellationToken).ConfigureAwait(false);
        var rebuilds = previous.Inspection.State is CacheState.Corrupt or CacheState.UnsupportedSchema ? 1 : 0;
        WorldQueryTelemetry Telemetry(bool published) => new(0, 1, rebuilds, parsedArtifacts, reusedArtifacts, scannedSources, reusedSources, clock.ElapsedMilliseconds, published);
        if (!completion.Finished || cancellationToken.IsCancellationRequested) return new(null, previous.Inspection, completion, Telemetry(false));
        var current = new WorldQueryContext(inventory, request, cancellationToken);
        if (!Unchanged(context, current))
            throw new ArtifactConflictException("Source snapshot changed before cache publication");
        var coverage = context.Extraction.Discovery.DiagnosticCode is null && descriptors.All(artifact => artifact.CoverageComplete)
            && context.Inputs.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant);
        var manifest = new ScanCacheManifest(inventory.WorldId, request.Scan.Scope, context.Versions, context.SnapshotId, descriptors.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
            context.Inputs, coverage, context.Extraction.Discovery.DiagnosticCode);
        var encoded = ScanCacheJson.WriteManifest(manifest); _ = ScanCacheJson.ReadManifest(encoded); cancellationToken.ThrowIfCancellationRequested();
        store.PublishManifest(Encoding.UTF8.GetBytes(encoded), previous.HeadHash);
        return new(View(manifest, context), previous.Inspection, completion, Telemetry(true));
    }
    private static bool Unchanged(WorldQueryContext before, WorldQueryContext after)
        => before.SnapshotId == after.SnapshotId && before.Extraction.Discovery.DiagnosticCode == after.Extraction.Discovery.DiagnosticCode;
    private WorldQuerySnapshot View(ScanCacheManifest manifest, WorldQueryContext context)
    {
        IEnumerable<TextSourceExtraction> Read()
        {
            foreach (var descriptor in manifest.Artifacts)
            {
                if (!context.Groups.TryGetValue(descriptor.RelativePath, out var sources)) throw new InvalidDataException("Snapshot source binding is missing");
                foreach (var source in CachedArtifactResults.Bind(CachedArtifactResults.Read(store, descriptor, inventory.WorldId), sources).Sources) yield return source;
            }
        }
        return new(manifest, Read, context.Extraction.StorageRules, context.Extraction.Discovery, context.Extraction.Limits, context.Extraction.StorageDisplays);
    }
    private static bool CacheFailure(Exception error) => error is IOException or InvalidDataException or JsonException or NotSupportedException or ArgumentException or OverflowException;
}
