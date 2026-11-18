using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace NBTExplorerCLI.Benchmarks;

public sealed record ScanWorkloadCounts(int Sources, long Occurrences, long TranslationKeys, long CommandReferences,
    int Failed, int Unsupported, int Cancelled, int IncompleteTextSources, int UnresolvedReferenceSources);
public sealed record ScanCost(double WallMilliseconds, double CpuMilliseconds, long ManagedAllocatedBytes, long PeakWorkingSetBytes,
    int HandlesBefore, int HandlesAfter, long SourceReads, long SourceReadBytes, long UniqueSourceBytes, long CacheReadBytes, long CacheSubmittedBytes);
public sealed record ScanSample(string SemanticHash, string OccurrenceHash, bool CoverageComplete, ScanWorkloadCounts Counts,
    IReadOnlyDictionary<string, int> Diagnostics, ScanCost Cost, CacheInspection Before, WorldQueryTelemetry Query, ScanExecutionTelemetry? Execution);

public static class ScanMeasurement
{
    public static async Task<ScanSample> RunAsync(IWorldInventory inventory, IScanCacheStore cache, ScanRequest request,
        ScanExecutionOptions execution, bool forceFull, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory); ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(execution);
        execution.Validate(); cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This measurement targets Windows process working set and handle counters");
        var sources = new MeasuredInventory(inventory); var state = new MeasuredCache(cache);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var handlesBefore = process.HandleCount; var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true); var clock = Stopwatch.StartNew();
        var query = await new WorldQueryService(sources, state).QueryAsync(new(request, Execution: execution, ForceFull: forceFull), cancellationToken);
        var snapshot = query.Snapshot ?? throw new OperationCanceledException("Query produced no completed snapshot", cancellationToken);
        var count = 0; long occurrences = 0, keys = 0, references = 0;
        var failed = 0; var unsupported = 0; var cancelled = 0; var incomplete = 0; var unresolved = 0;
        var diagnostics = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (snapshot.Manifest.CatalogDiagnosticCode is { } catalogCode) diagnostics["catalog:" + catalogCode] = 1;
        using var identities = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in snapshot.ReadSources())
        {
            cancellationToken.ThrowIfCancellationRequested(); count++;
            if (row.Outcome == SourceOutcome.Failed) failed++;
            if (row.Outcome == SourceOutcome.Unsupported) unsupported++;
            if (row.Outcome == SourceOutcome.Cancelled) cancelled++;
            if (row.DiagnosticCode is not null || row.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported or SourceOutcome.Cancelled)
            {
                var code = row.DiagnosticCode ?? row.Outcome.ToString();
                diagnostics[code] = diagnostics.GetValueOrDefault(code) + 1;
            }
            if (row.Text is { } text)
            {
                occurrences += text.Occurrences.Length; keys += text.TranslationKeys.Length;
                if (!text.Complete) incomplete++;
                foreach (var occurrence in text.Occurrences)
                    identities.AppendData(Convert.FromHexString(SourceIdentity.Occurrence(occurrence.Address).Digest.Hex));
            }
            if (row.Commands is { } commands)
            {
                references += commands.References.Length;
                if (commands.ReferenceCompleteness != ReferenceCompleteness.Complete) unresolved++;
            }
        }
        // Shard hashes bind every decoded value, address and diagnostic without retaining all world text.
        var semantic = Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(new
        {
            snapshotId = snapshot.Manifest.SnapshotId.Digest.Hex, snapshot.Manifest.CoverageComplete, snapshot.Manifest.CatalogDiagnosticCode,
            artifacts = snapshot.Manifest.Artifacts.Select(item => new { item.RelativePath, hash = item.ContentHash.Hex,
                item.SourceCount, item.TextCount, item.ReferenceCount, item.CoverageComplete })
        }));
        var occurrenceHash = new Hash256(Convert.ToHexString(identities.GetHashAndReset()));
        clock.Stop(); var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        process.Refresh();
        var cost = new ScanCost(clock.Elapsed.TotalMilliseconds, (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            allocated, process.PeakWorkingSet64, handlesBefore, process.HandleCount,
            sources.Reads, sources.ReadBytes, sources.UniqueBytes, state.ReadBytes, state.SubmittedBytes);
        return new(semantic.Hex, occurrenceHash.Hex, snapshot.Manifest.CoverageComplete && failed == 0 && unsupported == 0 && cancelled == 0,
            new(count, occurrences, keys, references, failed, unsupported, cancelled, incomplete, unresolved), diagnostics, cost, query.Before, query.Telemetry, query.Scan?.Telemetry);
    }
}
