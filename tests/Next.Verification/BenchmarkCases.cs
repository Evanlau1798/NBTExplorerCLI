namespace Next.Verification;

internal static class BenchmarkCases
{
    internal static readonly VerificationCase[] Cases =
    [
        new("benchmark-public-cancellation-result", "benchmark", _ => BenchmarkCommandTests.CancelledCommandReturnsStructuredFailureBeforeCacheCreation()),
        new("benchmark-public-existing-corpus-command", "benchmark", _ => BenchmarkCommandTests.ExistingCorpusGenerationRetainsItsFrozenOutput()),
        new("benchmark-scan-catalog-diagnostic", "benchmark", _ => ScanMeasurementTests.CatalogDiagnosticsRemainVisibleWhenEveryReturnedSourceIsGood()),
        new("benchmark-public-scan-command", "benchmark", _ => BenchmarkCommandTests.PublicMeasurementUsesRealSourcesAndKeepsIncompleteResults()),
        new("benchmark-public-invalid-state-boundary", "benchmark", _ => BenchmarkCommandTests.InvalidCommandsCannotCreateStateOrWriteWorlds()),
        new("benchmark-scan-full-warm-worker-truth", "benchmark", _ => ScanMeasurementTests.FullWarmAndWorkersRetainSemanticsAndActualBoundaryCounters()),
        new("benchmark-scan-change-failure-truth", "benchmark", _ => ScanMeasurementTests.ChangedAndBrokenSourcesCannotBecomeFasterCompleteResults()),
        new("benchmark-scan-invalid-pre-cancel", "benchmark", _ => ScanMeasurementTests.InvalidLimitsAndPrecancelRefuseBeforeAnySourceWork()),
    ];
}
