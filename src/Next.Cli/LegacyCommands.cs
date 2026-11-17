using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class LegacyCommands
{
    internal static CliResult Execute(CliArguments request)
    {
        var inputMode = request.Flag("input"); var create = request.Flag("create-session"); var list = request.Flag("list");
        if ((inputMode ? 1 : 0) + (request.Flag("import-id") ? 1 : 0) + (list ? 1 : 0) != 1) throw new CliFailure("INVALID_ARGUMENTS", "Choose exactly one --input, --import-id or --list");
        if (list && create) throw new CliFailure("INVALID_ARGUMENTS", "Listing does not create a session");
        if (!inputMode && new[] { "profile", "source-language", "target-language", "acknowledge-world-rebind" }.Any(request.Flag)) throw new CliFailure("INVALID_ARGUMENTS", "Stored reports retain their profile, languages and acknowledgement");
        var profile = inputMode ? Profile(request.Get("profile") ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --profile")) : LegacyInputProfile.Unknown;
        var id = request.Get("import-id") is { } declared ? new Hash256(declared) : null;
        var input = inputMode ? CliInput.ReadBytes(request.Get("input")!, LegacyInputReader.MaximumInputBytes) : null;
        var context = new WriteContext(request.Input!, request); string? source = null; string? target = null;
        if (inputMode)
        {
            source = request.Get("source-language"); target = request.Get("target-language");
            var configuration = source is null || target is null ? AgentCommands.Configuration(context) : null;
            source ??= configuration?.SourceLanguage ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply --source-language or confirmed agent defaults");
            target ??= configuration?.TargetLanguage ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply --target-language or confirmed agent defaults");
        }
        var snapshot = inputMode || create ? context.Query() : null;
        if (inputMode) id = LegacyImportIdentity.Compute(profile, Hash256.Compute(input!), context.Inventory.WorldId, snapshot!.Manifest.SnapshotId, source!, target!, request.Flag("acknowledge-world-rebind"));
        var offset = request.Number("offset", 0, 0, 2_000_000); var limit = request.Number("limit", 100, 1, 10_000);
        LegacyImportService importer; LegacyImportBundle report;
        try
        {
            var store = new FileSystemLegacyImportStore(Path.Combine(context.State, "imports")); importer = new(store);
            if (list)
            {
                var reports = store.ListIds().Skip(offset).Take(limit).Select(importId => new { importId }).ToArray();
                return new(new { reports, offset, limit, scope = "import-repository", contentsValidated = false, freshness = "not-checked" });
            }
            report = inputMode ? importer.Import(profile, input!, snapshot!, source!, target!, request.Flag("acknowledge-world-rebind"))
                : LegacyImportJson.Read(CliInput.Decode(store.Read(id!) ?? throw new CliFailure("NOT_FOUND", "No retained report for the requested import identity", new { importId = id, repair = "retry-same-import-input" })));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException && error is not ArtifactConflictException && CliApplication.Code(error) != "RESOURCE_LIMIT")
        { throw new CliFailure("IMPORT_STATE_IO", error.Message, new { importId = id, repair = list ? "retry-import-list" : "retry-same-import-input", lookup = "import-legacy <world> --list", diagnosticCode = CliApplication.Code(error) }); }
        if (report.WorldId != context.Inventory.WorldId) throw new ArtifactConflictException("Legacy report belongs to a different world root");
        var code = report.Items.Any(item => item.Reason == "LegacyInputLimit") ? "RESOURCE_LIMIT"
            : report.Items.Any(item => item.Decision == LegacyImportDecision.Rejected) ? "MALFORMED_SOURCE"
            : report.Items.Any(item => item.Decision == LegacyImportDecision.Conflict) ? "CONFLICT"
            : report.Items.Any(item => item.Decision == LegacyImportDecision.Unsupported) ? "UNSUPPORTED_SOURCE"
            : report.Items.Any(item => item.Decision == LegacyImportDecision.RescanRequired) ? "INCOMPLETE_SOURCE" : null;
        Guid? session = null; Guid? projected = null; SessionStatus? status = null; string? diagnosticCode = null;
        var repair = code is null ? null : "review-import-report";
        var freshness = snapshot is null ? "not-checked" : snapshot.Manifest.SnapshotId == report.SnapshotId ? "fresh" : "stale";
        if (create && code is null)
        {
            if (report.Items.Any(item => item.Decision != LegacyImportDecision.Mapped)) { code = "UNSUPPORTED_SOURCE"; repair = "review-retained-metadata"; }
            else
            {
                projected = LegacyImportService.SessionId(report.ImportId);
                if (freshness != "fresh") { code = "CONFLICT"; repair = "import-again-from-current-world"; }
                else
                {
                    try
                    {
                        var sessions = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(context.State, "sessions")));
                        session = importer.CreateSession(report, snapshot!, sessions); status = sessions.Status(session.Value);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or SessionReplyRejectedException or ArgumentException)
                    {
                        diagnosticCode = CliApplication.Code(error); code = error is ArtifactConflictException or SessionReplyRejectedException ? "CONFLICT" : diagnosticCode == "RESOURCE_LIMIT" ? "RESOURCE_LIMIT" : "IMPORT_SESSION_FAILED";
                        repair = "inspect-or-resume-import-session";
                    }
                }
            }
        }
        var data = new { report = new { report.ImportId, report.Profile, report.InputHash, report.WorldId, report.SnapshotId, report.SourceLocale, report.TargetLocale, report.RebindAcknowledged, report.Complete,
                counts = Enum.GetValues<LegacyImportDecision>().Select(decision => new { decision, count = report.Items.Count(item => item.Decision == decision) }).ToArray(),
                items = report.Items.Skip(offset).Take(limit).Select(item => new { item.Index, item.LegacyId, item.Original, item.Translation, item.LegacyPath, item.Decision, item.Reason,
                    candidateCount = item.Candidates.Length, item.LegacySourceHash, item.CurrentSourceHash, item.SlotId }).ToArray(), pagination = new { offset, limit, total = report.Items.Length } },
            sessionId = session, projectedSessionId = projected, status, freshness, worldCoverageComplete = snapshot?.Manifest.CoverageComplete, repair, diagnosticCode, validationScope = "legacy-retention-and-candidate-mapping" };
        var exit = code switch { null => 0, "CONFLICT" => 3, "UNSUPPORTED_SOURCE" or "INCOMPLETE_SOURCE" => 4, "RESOURCE_LIMIT" => 6, _ => 5 };
        return new(data, code, exit == 0 ? "Success" : exit == 3 ? "Conflict" : exit == 4 ? "Unsupported" : "Failed", exit, LegacyImportJson.Write(report));
    }
    private static LegacyInputProfile Profile(string name) => name switch
    {
        "command-session" => LegacyInputProfile.CommandSession, "translation-session" => LegacyInputProfile.TranslationSession, "reply-array" => LegacyInputProfile.ReplyArray,
        "glossary" => LegacyInputProfile.Glossary, "key-mapping" => LegacyInputProfile.KeyMapping, "agent-config" => LegacyInputProfile.AgentConfig,
        "questionnaire" => LegacyInputProfile.Questionnaire, "notes-text" => LegacyInputProfile.NotesText, "scan-cache" => LegacyInputProfile.ScanCache, "unknown" => LegacyInputProfile.Unknown,
        _ => throw new CliFailure("INVALID_ARGUMENTS", "Unknown legacy input profile")
    };
}
