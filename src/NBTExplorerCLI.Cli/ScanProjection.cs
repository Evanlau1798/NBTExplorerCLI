using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Cli;

internal static class ScanProjection
{
    internal static CliResult Create(WorldQueryResult query, ScanOptions options, CliArguments request)
    {
        var snapshot = query.Snapshot ?? throw new CliFailure("INCOMPLETE_SOURCE", "Scan was interrupted before publication");
        var sources = new List<object>(); var items = new List<TextOccurrence>(); var groups = new Dictionary<string, List<TextOccurrence>>(StringComparer.Ordinal);
        var sourceCount = 0; var textCount = 0; var matched = 0; var keyCount = 0; var references = 0; var unsupported = 0; var failed = 0;
        var resource = ReadCommands.IsResourceLimit(snapshot.Manifest.CatalogDiagnosticCode); var unreadable = ReadFailure(snapshot.Manifest.CatalogDiagnosticCode); long retained = 0;
        var extracting = request.Command == "extract"; var all = request.Flag("all"); var unique = request.Flag("unique");
        void Charge(TextOccurrence occurrence)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(occurrence.Address, (long)occurrence.Text.Length + occurrence.Context.Length)
                + SourceIdentity.Occurrence(occurrence.Address).Canonical.Length * 2L);
            if (retained > DecodeLimits.Default.MaxDecodedBytes) throw new CliFailure("RESOURCE_LIMIT", "Query result exceeds retained address/text budget");
        }
        foreach (var source in snapshot.ReadSources())
        {
            if (sourceCount >= options.Offset && sources.Count < options.Limit && !request.Flag("summary"))
                sources.Add(new { source.Source.Location, source.Source.Chunk, source.Outcome, source.DiagnosticCode, textComplete = source.Text?.Complete,
                    commandReferenceCompleteness = source.Commands?.ReferenceCompleteness });
            sourceCount++; resource |= ReadCommands.IsResourceLimit(source.DiagnosticCode) || source.DiagnosticCode == "CacheSerializationLimit";
            unreadable |= ReadFailure(source.DiagnosticCode);
            if (source.Outcome == SourceOutcome.Unsupported) unsupported++;
            else if (source.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled) failed++;
            if (source.Text is not { } text) continue;
            keyCount += text.TranslationKeys.Length; references += source.Commands?.References.Length ?? 0;
            foreach (var occurrence in text.Occurrences)
            {
                textCount++;
                if (extracting && !options.Matches(occurrence.Text)) continue;
                if (unique)
                {
                    Charge(occurrence);
                    if (!groups.TryGetValue(occurrence.Text, out var instances)) groups.Add(occurrence.Text, instances = []);
                    instances.Add(occurrence);
                }
                else if ((extracting || request.Flag("show-items")) && (all || matched >= options.Offset && items.Count < options.Limit)) { Charge(occurrence); items.Add(occurrence); }
                matched++;
            }
        }
        var complete = snapshot.Manifest.CoverageComplete && failed == 0 && unsupported == 0;
        object? texts = null;
        if (extracting) texts = unique ? groups.Skip(all ? 0 : options.Offset).Take(all ? int.MaxValue : options.Limit)
            .Select(pair => (object)new { text = pair.Key, count = pair.Value.Count, instances = pair.Value }).ToArray() : items;
        var total = unique ? groups.Count : matched;
        return Classify(new { worldId = snapshot.Manifest.WorldId, snapshotId = snapshot.Manifest.SnapshotId, snapshot.Manifest.Scope,
            coverageComplete = complete, referenceCoverage = "not-assessed", validationScope = request.Command == "validate" ? "decoded-text-sources" : null,
            catalogDiagnosticCode = snapshot.Manifest.CatalogDiagnosticCode, counts = new { sources = sourceCount, occurrences = textCount, matched, translationKeys = keyCount, commandReferences = references, unsupported, failed },
            sources, sourcePagination = new { offset = options.Offset, limit = options.Limit, total = sourceCount }, items = extracting ? null : items, texts,
            pagination = new { offset = all ? 0 : options.Offset, limit = all ? total : options.Limit, total }, telemetry = query.Telemetry, cacheBefore = query.Before,
            execution = query.Scan?.Telemetry }, complete, resource, unreadable, failed > 0, unsupported > 0);
    }
    internal static CliResult Classify(object data, bool complete, bool resource, bool unreadable, bool failed, bool unsupported)
    {
        var code = resource ? "RESOURCE_LIMIT" : unreadable ? "READ_ERROR" : failed ? "MALFORMED_SOURCE" : unsupported ? "UNSUPPORTED_SOURCE" : !complete ? "INCOMPLETE_SOURCE" : null;
        var exit = resource ? 6 : unreadable || failed ? 5 : unsupported || !complete ? 4 : 0;
        return new(data, code, exit == 0 ? "Success" : exit == 4 ? "Unsupported" : "Failed", exit);
    }
    internal static bool ReadFailure(string? code) => code is "SourceReadFailed" or "PackMetadata:SourceReadFailed" or "MetadataReadFailed" or "DirectoryEnumerationFailed" or "RootEnumerationFailed";
}
