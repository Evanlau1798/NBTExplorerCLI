using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal static class SessionCommands
{
    internal static readonly string[] Subcommands = ["start", "status", "export", "advance", "submit", "get", "jump", "note", "glossary", "reset", "groups", "memory", "plan", "export-lang", "create-pack", "seal", "fallback", "help"];
    internal static string[] Allowed(string action)
    {
        if (action == "help") return ["help"];
        if (action == "fallback") return ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "format", "line",
            "byte-range", "snbt-profile", "text-pointer", "source-language", "target-language", "reply", "batch", "session-id", "state-root", "output", "help"];
        string[] options = action switch
        {
            "start" => ["source-language", "target-language", "operation-id", "page-size", "page-bytes", "match-pattern", "types", "path", "dimension", "region", "standalone", "no-group", "minecraft-version"],
            "status" => [], "export" => ["page", "operation-id", "no-group"], "advance" => ["reply", "no-group"], "submit" => ["reply", "no-group"],
            "get" => ["slot-id"], "jump" => ["request"], "note" or "glossary" => ["request", "limit", "offset"],
            "reset" => ["request", "confirm"], "groups" => ["limit", "offset"], "memory" => ["slot-id", "source-sessions", "glossary-version", "limit", "offset"],
            "plan" => ["direct", "keys", "prefix", "language-output", "revision", "operation-id", "synchronize-references"], "seal" => ["revision", "operation-id", "plan-hash"],
            "export-lang" => ["revision", "original-lang", "target-lang", "prefix", "plan-output", "apply", "world-offline"],
            "create-pack" => ["revision", "original-lang", "target-lang", "prefix", "plan-output", "apply", "world-offline", "name", "format", "merge", "pack-format"],
            _ => throw new CliFailure("UNKNOWN_COMMAND", "Unknown translate subcommand")
        };
        return ["session-id", "state-root", "output", "help", .. options];
    }
    internal static CliResult Execute(CliArguments request)
    {
        if (request.Subcommand == "fallback") return FallbackCommands.Execute(request);
        if (request.Subcommand is "export-lang" or "create-pack") return ResourceCommands.Execute(request);
        var id = request.Get("session-id") is { } declared ? GuidValue(declared) : request.Subcommand == "start" ? Guid.NewGuid() : throw new CliFailure("INVALID_ARGUMENTS", "Missing --session-id");
        var operationId = request.Get("operation-id") is { } declaredOperation ? GuidValue(declaredOperation) : Guid.NewGuid();
        if (request.Subcommand is "plan" or "seal") return SessionWorldCommands.Execute(request, id, operationId);
        ScanOptions? options = null; SessionPolicy? policy = null; string? sourceLanguage = null; string? targetLanguage = null;
        if (request.Subcommand == "start")
        {
            options = new ScanOptions(request); policy = new(request.Number("page-size", 30, 1, 100_000), request.Number("page-bytes", 65_536, 1024, 67_108_864),
                GroupRelatedText: !request.Flag("no-group"));
            sourceLanguage = TranslationLanguages.SourceOption(request.Get("source-language")); targetLanguage = request.Get("target-language");
        }
        var context = new WriteContext(request.Input!, request);
        if (request.Subcommand == "start")
        {
            if (!request.Flag("page-size") && AgentCommands.Configuration(context) is { } configuration)
                policy = policy! with { MaximumPageSlots = configuration.PreferredItemsPerPage };
        }
        var service = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(context.State, "sessions")));
        if (request.Subcommand == "start")
        {
            var existing = request.Flag("session-id") ? service.TryRead(id) : null;
            targetLanguage = targetLanguage is null && existing is not null ? existing.TargetLocale : TranslationLanguages.Target(targetLanguage);
            Func<IReadOnlyList<TextOccurrence>, string>? resolveSource = null;
            if (sourceLanguage == TranslationLanguages.Automatic)
            {
                if (existing is not null) sourceLanguage = existing.SourceLocale;
                else resolveSource = TranslationSourceDetection.Resolve;
            }
            var snapshot = Query(context, options!.Scope, classicVersion: request.Get("minecraft-version"));
            var start = TranslationSessionService.CreateStartRequest(snapshot, id, sourceLanguage!, targetLanguage, policy,
                item => options.Matches(item.Text), resolveSource);
            if (start.Slots.IsEmpty) throw new CliFailure("NOT_FOUND", "No text slots match the requested selection");
            var receipt = service.Start(start, operationId);
            try { Preferences(context).Create(id, !request.Flag("no-group")); }
            catch (Exception error) { throw new CliFailure(CliApplication.Code(error), error.Message, new { sessionId = id, receipt, repair = "retry-start-with-same-identities" }); }
            return new(new { receipt, status = service.Status(id), start.SourceLocale, start.TargetLocale,
                groups = Groups(service, id, request, context).Select(Group).ToArray() });
        }
        var state = service.Read(id);
        if (state.Source.WorldId != context.Inventory.WorldId) throw new ArtifactConflictException("Session belongs to a different world root");
        var groups = request.Subcommand is "export" or "advance" or "submit" ? Groups(service, id, request, context, state) : [];
        switch (request.Subcommand)
        {
            case "status":
                return Status(context, state);
            case "get":
                var item = TranslationSessionService.Get(state, new(Required(request, "slot-id"))); var slot = item.Slot;
                return new(new { slot = new { slotId = slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.Role, slot.SourceLocale, slot.TargetLocale,
                    slot.AllowedEmpty, placeholders = slot.Placeholders.Tokens, slot.Placeholders.ProtectVisibleText, slot.Placeholders.ProtectPrivateUseGlyphs, item.Format, item.State, item.Reason,
                    reply = item.Reply is { } translation ? new { translation.TargetLocale, translation.Text } : null } });
            case "export":
                var page = TranslationSessionService.Export(state, request.Number("page", state.Workspace?.CurrentPage ?? 0, 0, Math.Max(0, state.Pages.Length - 1)));
                var pageContext = state.Policy.GroupRelatedText ? groups.SelectMany(group => group.Slots.Select(slot => (slot, group)))
                    .ToDictionary(item => item.slot, item => item.group) : null;
                var prompt = SessionExchangeJson.Prompt(page, operationId, pageContext);
                if (Encoding.UTF8.GetByteCount(prompt) > state.Policy.MaximumPageBytes) throw new InvalidDataException("Stored session page exceeds its byte policy");
                return new(new { page = Json(SessionExchangeJson.WritePage(page)), replyTemplate = Json(SessionExchangeJson.ReplyTemplate(page, operationId)), prompt,
                    groups = groups.Select(Group).ToArray() }, Export: prompt);
            case "advance": case "submit":
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
                var status = service.Status(id); var pending = status.Pending + status.Conflict == 0 ? null : service.Advance(id);
                return new(new { receipt = accepted, status, pendingPage = pending is null ? (JsonElement?)null : Json(SessionExchangeJson.WritePage(pending)), groups = groups.Select(Group).ToArray() });
            case "groups": return new(new { groups = TranslationSessionService.Groups(state).Skip(request.Number("offset", 0, 0, 2_000_000)).Take(request.Number("limit", 100, 1, 10_000)).Select(Group).ToArray() });
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
    private static SessionContextGroup[] Groups(TranslationSessionService service, Guid id, CliArguments request, WriteContext context, TranslationSession? state = null)
        => request.Flag("no-group") || Preferences(context).Read(id) == false ? [] : TranslationSessionService.Groups(state ?? service.Read(id)).ToArray();
    private static object Group(SessionContextGroup group) => new { group.Kind, groupId = group.GroupId.Digest, memberDigest = group.MemberDigest.Digest, group.Slots };
    internal static SnapshotScope Scope(TranslationSession state) => new(state.Source.Dimensions, state.Source.Regions, state.Source.Types, state.Source.ArtifactPaths);
    internal static CliResult Status(WriteContext context, TranslationSession state)
    {
        var status = TranslationSessionService.Status(state);
        if (state.Slots.All(slot => slot.State == SessionSlotState.Applied))
            return new(new { status, sourceFresh = (bool?)null, snapshotId = state.Source.SnapshotId, freshness = "not-checked", validationScope = "sealed-session-history" });
        var current = Query(context, state); var fresh = current.Manifest.SnapshotId == state.Source.SnapshotId;
        return new(new { status, sourceFresh = fresh, snapshotId = state.Source.SnapshotId, currentSnapshotId = current.Manifest.SnapshotId,
            repair = fresh ? null : "start-new-session" }, fresh ? null : "CONFLICT", fresh ? "Success" : "Conflict", fresh ? 0 : 3);
    }
    internal static WorldQuerySnapshot Query(WriteContext context, TranslationSession state)
        => Query(context, Scope(state), classicVersion: MinecraftProfiles.ClassicVersion(state.Source.Versions.Profile));
    internal static WorldQuerySnapshot Query(WriteContext context, SnapshotScope scope, WorldQueryPurpose purpose = WorldQueryPurpose.Session, int workers = 2, string? classicVersion = null)
    {
        var result = new WorldQueryService(context.Inventory, new FileSystemScanCacheStore(Path.Combine(context.State, "cache")))
            .QueryAsync(new(new(scope, ClassicVersion: classicVersion), purpose, new(Workers: workers))).GetAwaiter().GetResult();
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
            throw new CliFailure(classified.Code!, purpose == WorldQueryPurpose.Convert ? "Conversion requires complete selected source coverage" : "Session requires complete selected source coverage", classified.Data);
        }
        return snapshot;
    }
    internal static void RequireFresh(WriteContext context, TranslationSession state)
    {
        if (Query(context, state).Manifest.SnapshotId != state.Source.SnapshotId)
            throw new CliFailure("CONFLICT", "World sources changed; retain this session and start a new session from current sources", new { sessionId = state.SessionId, repair = "start-new-session" });
    }
    private static Guid GuidValue(string text) => Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw new CliFailure("INVALID_ARGUMENTS", "Use a nonempty GUID");
    private static string Required(CliArguments request, string option) => request.Get(option) ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --" + option);
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 }); return document.RootElement.Clone(); }
}
