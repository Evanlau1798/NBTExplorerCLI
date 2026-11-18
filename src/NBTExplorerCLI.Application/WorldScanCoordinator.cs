using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

public sealed class WorldScanCoordinator
{
    private readonly WorldTextExtraction extraction;
    private readonly ScanExecutionOptions options;
    public WorldScanCoordinator(WorldTextExtraction extraction, ScanExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extraction); this.extraction = extraction;
        this.options = options ?? new(); this.options.Validate();
    }
    public Task<ScanCompletion> RunAsync(Func<ArtifactScanBatch, CancellationToken, ValueTask> store, CancellationToken cancellationToken = default)
        => RunCoreAsync(store, cancellationToken, null);
    internal Task<ScanCompletion> RunCachedAsync(Func<ArtifactScanBatch, CancellationToken, ValueTask> store, Func<ImmutableArray<DiscoveredSource>, ArtifactScanBatch?> reuse, CancellationToken cancellationToken)
        => RunCoreAsync(store, cancellationToken, reuse);
    private async Task<ScanCompletion> RunCoreAsync(Func<ArtifactScanBatch, CancellationToken, ValueTask> store, CancellationToken cancellationToken, Func<ImmutableArray<DiscoveredSource>, ArtifactScanBatch?>? reuse)
    {
        ArgumentNullException.ThrowIfNull(store);
        var groups = extraction.ScanSources.GroupBy(source => source.Location.Artifact.RelativePath, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.ToImmutableArray()).ToArray();
        var sizes = extraction.Discovery.Inventory.Entries.ToDictionary(entry => entry.RelativePath, entry => entry.Bytes, StringComparer.Ordinal);
        var results = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(options.QueuedArtifacts) { SingleReader = true });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); var token = cancellation.Token;
        var budget = new ScanBufferBudget(options.InFlightBytes); var completed = new HashSet<string>(StringComparer.Ordinal);
        var queuePeak = 0; var claimIndex = -1; var active = 0; var activePeak = 0; var remaining = options.Workers; var storedSources = 0; Exception? failure = null;
        void Fail(Exception error) { Interlocked.CompareExchange(ref failure, error, null); cancellation.Cancel(); }
        var workers = Enumerable.Range(0, options.Workers).Select(_ => Task.Run(async () =>
        {
            try
            {
                int index;
                while ((index = Interlocked.Increment(ref claimIndex)) < groups.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var group = groups[index];
                    var path = group[0].Location.Artifact.RelativePath;
                    var reservation = group.Any(source => source.Outcome == SourceOutcome.Success) ? checked(sizes.GetValueOrDefault(path) + extraction.Limits.MaxDecodedBytes) : 0;
                    IDisposable? lease = null;
                    try
                    {
                        ArtifactScanBatch batch;
                        if (reservation > options.InFlightBytes) batch = LimitBatch(group);
                        else
                        {
                            lease = await budget.AcquireAsync(reservation, token).ConfigureAwait(false);
                            Maximum(ref activePeak, Interlocked.Increment(ref active));
                            try { batch = reuse?.Invoke(group) ?? Read(group, token); }
                            finally { Interlocked.Decrement(ref active); }
                        }
                        await results.Writer.WriteAsync(new(batch, lease), token).ConfigureAwait(false); lease = null;
                        Maximum(ref queuePeak, results.Reader.Count);
                    }
                    finally { lease?.Dispose(); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Fail(error); }
            finally { if (Interlocked.Decrement(ref remaining) == 0) results.Writer.TryComplete(); }
        }, CancellationToken.None)).ToArray();
        await foreach (var envelope in results.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            using (envelope.Lease)
            {
                if (token.IsCancellationRequested) continue;
                try
                {
                    await store(envelope.Batch, token).ConfigureAwait(false);
                    completed.Add(envelope.Batch.RelativePath); storedSources += envelope.Batch.Sources.Length;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) { Fail(error); }
            }
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        var unfinished = groups.Where(group => !completed.Contains(group[0].Location.Artifact.RelativePath)).SelectMany(group => group)
            .Select(source => new TextSourceExtraction(source, SourceOutcome.Cancelled, null, "ScanCancelled")).ToImmutableArray();
        return new(!token.IsCancellationRequested && unfinished.IsEmpty, completed.Order(StringComparer.Ordinal).ToImmutableArray(), unfinished,
            new(options.Workers, queuePeak, budget.Peak, activePeak, completed.Count, storedSources));
    }
    private ArtifactScanBatch Read(ImmutableArray<DiscoveredSource> sources, CancellationToken token)
    {
        var results = ImmutableArray.CreateBuilder<TextSourceExtraction>(); long retained = 0;
        using var reader = extraction.ReadGroup(sources).GetEnumerator();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!reader.MoveNext()) break;
            var source = reader.Current;
            if (source.Text is not null) retained = checked(retained + TextExtractionBudget.ResultBytes(source.Text, source.Commands?.References));
            if (retained > extraction.Limits.MaxDecodedBytes) return LimitBatch(sources);
            results.Add(source);
        }
        return new(sources[0].Location.Artifact.RelativePath, results.ToImmutable());
    }
    private static ArtifactScanBatch LimitBatch(ImmutableArray<DiscoveredSource> sources)
        => new(sources[0].Location.Artifact.RelativePath, sources.Select(source => source.Outcome == SourceOutcome.Success
            ? new TextSourceExtraction(source, SourceOutcome.Failed, null, "LimitExceeded") : new(source, source.Outcome, null, source.DiagnosticCode)).ToImmutableArray());
    private static void Maximum(ref int target, int value)
    {
        int before;
        do { before = Volatile.Read(ref target); if (before >= value) return; } while (Interlocked.CompareExchange(ref target, value, before) != before);
    }
    private sealed record Envelope(ArtifactScanBatch Batch, IDisposable? Lease);
}
