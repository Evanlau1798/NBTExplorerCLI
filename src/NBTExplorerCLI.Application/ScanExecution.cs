using System.Collections.Immutable;

namespace NBTExplorerCLI.Application;

public sealed record ScanExecutionOptions(int Workers = 2, int QueuedArtifacts = 16, long InFlightBytes = 268_435_456)
{
    public void Validate()
    {
        if (Workers is < 1 or > 8 || QueuedArtifacts is < 1 or > 16 || InFlightBytes is < 1 or > 268_435_456)
            throw new ArgumentOutOfRangeException(nameof(ScanExecutionOptions));
    }
}
public sealed record ArtifactScanBatch(string RelativePath, ImmutableArray<TextSourceExtraction> Sources);
public sealed record ScanExecutionTelemetry(int Workers, int ObservedQueuePeak, long PeakReservedBytes, int PeakActiveWorkers, int StoredArtifacts, int StoredSources);
public sealed record ScanCompletion(bool Finished, ImmutableArray<string> CompletedArtifacts, ImmutableArray<TextSourceExtraction> UnfinishedSources, ScanExecutionTelemetry Telemetry);
