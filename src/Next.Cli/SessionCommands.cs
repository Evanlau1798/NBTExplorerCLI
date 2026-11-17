using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class SessionCommands
{
    internal static readonly string[] Subcommands = ["start", "status", "export", "next", "submit", "get", "jump", "note", "glossary", "reset", "groups", "memory", "plan", "apply", "apply-keys", "seal", "help"];
    internal static string[] Allowed(string action)
    {
        if (action == "help") return ["help"];
        string[] options = action switch
        {
            "start" => ["source-language", "target-language", "operation-id", "page-size", "page-bytes", "match-pattern", "types", "path", "dimension", "region", "standalone", "no-group"],
            "status" => [], "export" => ["page", "operation-id", "no-group"], "next" => ["reply", "no-group"], "submit" => ["reply", "no-group"],
            "get" => ["slot-id"], "jump" => ["request"], "note" or "glossary" => ["request", "limit", "offset"],
            "reset" => ["request", "confirm"], "groups" => ["limit", "offset"], "memory" => ["slot-id", "source-sessions", "glossary-version", "limit", "offset"],
            "plan" or "apply" or "apply-keys" => ["direct", "keys", "prefix", "language-output", "revision", "operation-id", "synchronize-references"], "seal" => ["revision", "operation-id", "plan-hash"],
            _ => throw new CliFailure("UNKNOWN_COMMAND", "Unknown translate subcommand")
        };
        return ["session-id", "state-root", "output", "help", .. options];
    }
    internal static CliResult Execute(CliArguments request)
    {
        var id = request.Get("session-id") is { } declared ? GuidValue(declared) : request.Subcommand == "start" ? Guid.NewGuid() : throw new CliFailure("INVALID_ARGUMENTS", "Missing --session-id");
        var operationId = request.Get("operation-id") is { } declaredOperation ? GuidValue(declaredOperation) : Guid.NewGuid();
        if (request.Subcommand is "apply" or "apply-keys")
        {
            var choices = new Dictionary<string, string?>(request.Options);
            if (!request.Flag("direct") || request.Subcommand == "apply-keys") choices.TryAdd("keys", null);
            var preview = SessionWorldCommands.Execute(request with { Subcommand = "plan", Options = choices }, id, operationId);
            var plan = (SessionWorldCommands.PlanPreview)preview.Data;
            string[] aliasStateArguments = request.Get("state-root") is { } root ? ["--state-root", Path.GetFullPath(root)] : [];
            var world = Path.GetFullPath(request.Input!); var stateTemplate = aliasStateArguments.Length == 0 ? "" : " --state-root <state-root>";
            string[] commitArguments = ["apply", world, "--plan", request.Get("output") is { } file ? Path.GetFullPath(file) : "<retained-plan>", "--world-offline", .. aliasStateArguments];
            string[] completeArguments = ["translate", "seal", world, "--session-id", id.ToString(), "--revision", plan.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--operation-id", operationId.ToString(), "--plan-hash", plan.PlanHash.Hex, .. aliasStateArguments];
            return preview with { Data = new { preview = true, result = preview.Data, migration = new { alias = request.Subcommand, canonicalCommand = "translate plan", requiresExplicitApply = true,
                commitCommand = "apply <world> --plan <retained-plan> --world-offline" + stateTemplate,
                completeCommand = "translate seal <world> --session-id <session-id> --revision <revision> --operation-id <operation-id> --plan-hash <hash>" + stateTemplate,
                commitArguments, completeArguments } } };
        }
        if (request.Subcommand is "plan" or "seal") return SessionWorldCommands.Execute(request, id, operationId);
        ScanOptions? options = null; SessionPolicy? policy = null; string? sourceLanguage = null; string? targetLanguage = null;
        if (request.Subcommand == "start")
        {
            options = new ScanOptions(request); policy = new(request.Number("page-size", 30, 1, 100_000), request.Number("page-bytes", 65_536, 1024, 67_108_864));
            sourceLanguage = request.Get("source-language"); targetLanguage = request.Get("target-language");
        }
        var context = new WriteContext(request.Input!, request);
        if (request.Subcommand == "start")
        {
            var configuration = sourceLanguage is null || targetLanguage is null || !request.Flag("page-size") ? AgentCommands.Configuration(context) : null;
            sourceLanguage ??= configuration?.SourceLanguage ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply --source-language or confirm an agent configuration");
            targetLanguage ??= configuration?.TargetLanguage ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply --target-language or confirm an agent configuration");
            if (!request.Flag("page-size") && configuration is not null) policy = policy! with { MaximumPageSlots = configuration.PreferredItemsPerPage };
        }
        var service = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(context.State, "sessions")));
        if (request.Subcommand == "start")
        {
            var snapshot = Query(context, options!.Scope);
            var start = TranslationSessionService.CreateStartRequest(snapshot, id, sourceLanguage!, targetLanguage!, policy, item => options.Matches(item.Text));
            if (start.Slots.IsEmpty) throw new CliFailure("NOT_FOUND", "No text slots match the requested selection");
            var receipt = service.Start(start, operationId);
            try { Preferences(context).Create(id, !request.Flag("no-group")); }
            catch (Exception error) { throw new CliFailure(CliApplication.Code(error), error.Message, new { sessionId = id, receipt, repair = "retry-start-with-same-identities" }); }
            return new(new { receipt, status = service.Status(id), groups = Groups(service, id, request, context) });
        }
        var state = service.Read(id);
        if (state.Source.WorldId != context.Inventory.WorldId) throw new ArtifactConflictException("Session belongs to a different world root");
        var groups = request.Subcommand is "export" or "next" or "submit" ? Groups(service, id, request, context) : [];
        switch (request.Subcommand)
        {
            case "status":
                return Status(context, state);
            case "get":
                var item = service.Get(id, new(Required(request, "slot-id"))); var slot = item.Slot;
                return new(new { slot = new { slotId = slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.Role, slot.SourceLocale, slot.TargetLocale,
                    slot.AllowedEmpty, placeholders = slot.Placeholders.Tokens, item.Format, item.State, item.Reason,
                    reply = item.Reply is { } translation ? new { translation.TargetLocale, translation.Text } : null } });
            case "export":
                var page = service.Export(id, request.Number("page", state.Workspace?.CurrentPage ?? 0, 0, Math.Max(0, state.Pages.Length - 1)));
                var prompt = SessionExchangeJson.Prompt(page, operationId);
                return new(new { page = Json(SessionExchangeJson.WritePage(page)), replyTemplate = Json(SessionExchangeJson.ReplyTemplate(page, operationId)), prompt,
                    groups }, Export: prompt);
            case "next": case "submit":
                SessionOperationReceipt? accepted = null;
                if (request.Get("reply") is { } file)
                {
                    var bytes = Encoding.UTF8.GetBytes(CliInput.Read(file)); var reply = SessionExchangeJson.ReadReply(Encoding.UTF8.GetString(bytes));
                    if (reply.SessionId != id) throw new ArtifactConflictException("Reply belongs to a different session");
                    if (!state.Operations.Any(operation => operation.OperationId == reply.OperationId)) RequireFresh(context, state);
                    try { accepted = service.Submit(id, bytes); }
                    catch (SessionReplyRejectedException error)
                    { throw new CliFailure("CONFLICT", "Reply validation failed; export the current page and repair the complete slot set", new { issues = error.Issues, repair = "export-current-page", status = service.Status(id) }); }
                }
                else if (request.Subcommand == "submit") throw new CliFailure("INVALID_ARGUMENTS", "Missing --reply");
                var status = service.Status(id); var next = status.Pending + status.Conflict == 0 ? null : service.Next(id);
                return new(new { receipt = accepted, status, nextPage = next is null ? (JsonElement?)null : Json(SessionExchangeJson.WritePage(next)), groups });
            case "groups": return new(new { groups = service.Groups(id).Skip(request.Number("offset", 0, 0, 2_000_000)).Take(request.Number("limit", 100, 1, 10_000)).Select(Group).ToArray() });
            case "memory":
                var sources = Required(request, "source-sessions").Split(',').Select(GuidValue).ToArray();
                var candidates = service.Memory(id, new(Required(request, "slot-id")), sources, Required(request, "glossary-version"));
                return new(new { candidates = candidates.Skip(request.Number("offset", 0, 0, 2_000_000)).Take(request.Number("limit", 100, 1, 10_000))
                    .Select(candidate => new { key = candidate.Key.Digest, candidate.SessionId, candidate.SlotId, candidate.Revision, candidate.Text, candidate.Provenance }).ToArray(), total = candidates.Length });
            case "note" when !request.Flag("request"):
                return new(new { notes = (state.Workspace ?? SessionWorkspace.Empty).Notes.Skip(request.Number("offset", 0, 0, 2_000_000)).Take(request.Number("limit", 100, 1, 10_000)).ToArray() });
            case "glossary" when !request.Flag("request"):
                return new(new { glossary = (state.Workspace ?? SessionWorkspace.Empty).Glossary.Skip(request.Number("offset", 0, 0, 2_000_000)).Take(request.Number("limit", 100, 1, 10_000)).ToArray() });
            default:
                if (request.Flag("limit") || request.Flag("offset")) throw new CliFailure("INVALID_ARGUMENTS", "Pagination is only for metadata queries");
                var command = SessionCommandJson.Read(CliInput.Read(Required(request, "request")));
                if (command.SessionId != id || command.Kind.ToString().ToLowerInvariant() != request.Subcommand) throw new CliFailure("INVALID_ARGUMENTS", "Session command does not match the requested action or session");
                if (request.Subcommand == "reset" && !request.Flag("confirm")) return new(new { preview = service.PreviewReset(id, command.ResetSlots) });
                var result = service.Execute(command); return new(new { receipt = result, status = service.Status(id) });
        }
    }
    private static FileSystemSessionPreferencesStore Preferences(WriteContext context) => new(Path.Combine(context.State, "session-preferences"));
    private static object[] Groups(TranslationSessionService service, Guid id, CliArguments request, WriteContext context)
        => request.Flag("no-group") || Preferences(context).Read(id) == false ? [] : service.Groups(id).Select(Group).ToArray();
    private static object Group(SessionContextGroup group) => new { group.Kind, groupId = group.GroupId.Digest, memberDigest = group.MemberDigest.Digest, group.Slots };
    internal static SnapshotScope Scope(TranslationSession state) => new(state.Source.Dimensions, state.Source.Regions, state.Source.Types, state.Source.ArtifactPaths);
    internal static CliResult Status(WriteContext context, TranslationSession state)
    {
        var status = TranslationSessionService.Status(state);
        if (state.Slots.All(slot => slot.State == SessionSlotState.Applied))
            return new(new { status, sourceFresh = (bool?)null, snapshotId = state.Source.SnapshotId, freshness = "not-checked", validationScope = "sealed-session-history" });
        var current = Query(context, Scope(state)); var fresh = current.Manifest.SnapshotId == state.Source.SnapshotId;
        return new(new { status, sourceFresh = fresh, snapshotId = state.Source.SnapshotId, currentSnapshotId = current.Manifest.SnapshotId,
            repair = fresh ? null : "start-new-session" }, fresh ? null : "CONFLICT", fresh ? "Success" : "Conflict", fresh ? 0 : 3);
    }
    internal static WorldQuerySnapshot Query(WriteContext context, SnapshotScope scope)
    {
        var result = new WorldQueryService(context.Inventory, new FileSystemScanCacheStore(Path.Combine(context.State, "cache")))
            .QueryAsync(new(new(scope), WorldQueryPurpose.Session)).GetAwaiter().GetResult();
        var snapshot = result.Snapshot ?? throw new CliFailure("INCOMPLETE_SOURCE", "World query was interrupted");
        if (!snapshot.Manifest.CoverageComplete)
        {
            var failures = new List<object>(); var resource = ReadCommands.IsResourceLimit(snapshot.Manifest.CatalogDiagnosticCode);
            var unreadable = ScanProjection.ReadFailure(snapshot.Manifest.CatalogDiagnosticCode); var failed = false; var unsupported = false;
            foreach (var source in snapshot.ReadSources())
            {
                resource |= ReadCommands.IsResourceLimit(source.DiagnosticCode) || source.DiagnosticCode == "CacheSerializationLimit";
                unreadable |= ScanProjection.ReadFailure(source.DiagnosticCode); failed |= source.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled; unsupported |= source.Outcome == SourceOutcome.Unsupported;
                if (failures.Count < 100 && (source.DiagnosticCode is not null || source.Outcome != SourceOutcome.Success)) failures.Add(new { source.Source.Location, source.Outcome, source.DiagnosticCode });
            }
            var classified = ScanProjection.Classify(new { repair = "repair-source-and-retry", sources = failures, snapshot.Manifest.CatalogDiagnosticCode }, false, resource, unreadable, failed, unsupported);
            throw new CliFailure(classified.Code!, "Session requires complete selected source coverage", classified.Data);
        }
        return snapshot;
    }
    private static void RequireFresh(WriteContext context, TranslationSession state)
    {
        if (Query(context, Scope(state)).Manifest.SnapshotId != state.Source.SnapshotId)
            throw new CliFailure("CONFLICT", "World sources changed; retain this session and start a new session from current sources", new { sessionId = state.SessionId, repair = "start-new-session" });
    }
    private static Guid GuidValue(string text) => Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw new CliFailure("INVALID_ARGUMENTS", "Use a nonempty GUID");
    private static string Required(CliArguments request, string option) => request.Get(option) ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --" + option);
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 }); return document.RootElement.Clone(); }
}
