using System.Globalization;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class SessionWorldCommands
{
    internal sealed record PlanPreview(bool Preview, Guid SessionId, long Revision, Guid OperationId, JsonElement Plan, Hash256 PlanHash);
    internal static CliResult Execute(CliArguments request, Guid id, Guid operation)
    {
        if (!long.TryParse(request.Get("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) throw new CliFailure("INVALID_ARGUMENTS", "Supply an exact nonnegative --revision");
        var seal = request.Subcommand == "seal";
        if (seal && !request.Flag("operation-id")) throw new CliFailure("INVALID_ARGUMENTS", "Seal requires a retained --operation-id for exact retry");
        if (!seal && request.Flag("direct") == request.Flag("keys")) throw new CliFailure("INVALID_ARGUMENTS", "Choose exactly one planning mode: --direct or --keys");
        if (!seal && (request.Flag("keys") && request.Flag("synchronize-references") || !request.Flag("keys") && (request.Flag("prefix") || request.Flag("language-output")))) throw new CliFailure("INVALID_ARGUMENTS", "Key output options and reference synchronization belong to separate planning modes");
        var languagePath = !seal && request.Flag("keys") ? request.Get("language-output") ?? throw new CliFailure("INVALID_ARGUMENTS", "Key planning requires --language-output relative-path[#entry]") : null;
        var hash = seal ? new Hash256(request.Get("plan-hash") ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --plan-hash")) : null;
        object Repair(string instruction) => new { sessionId = id, revision, operationId = operation, planHash = hash, repair = instruction };
        try
        {
            var context = new WriteContext(request.Input!, request);
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(context.State, "sessions"))); var state = service.Read(id);
            if (state.Source.WorldId != context.Inventory.WorldId) throw new ArtifactConflictException("Session belongs to a different world root");
            if (!seal)
            {
                if (state.Revision != revision || state.Slots.Any(slot => slot.State is not (SessionSlotState.Translated or SessionSlotState.Unchanged)))
                    throw new CliFailure("CONFLICT", "Retain this session; inspect its revision and complete all pending replies before planning", Repair("inspect-session-status-and-complete-replies"));
                var snapshot = SessionCommands.Query(context, SessionCommands.Scope(state));
                if (snapshot.Manifest.SnapshotId != state.Source.SnapshotId) throw new CliFailure("CONFLICT", "World sources changed; retain this session and start a new session from current sources", Repair("start-new-session"));
                var literals = state.Slots.Select(slot => new LiteralTranslation(slot.Slot, slot.Reply ?? new(slot.Slot.Id, slot.Slot.SourceTextHash, slot.Slot.TargetLocale, slot.Slot.SourceText))).ToArray();
                var operations = literals.Select(translation => (PatchOperation)new LiteralPatch("session-" + operation.ToString("N") + "-" + translation.Slot.Id.Digest.Hex, translation)).ToArray();
                CommandOutputJson.ValidateRejectionBudget(operations.Select(item => item.Id).ToArray());
                var plan = WriteCommands.BeforeCommit(operations.Select(item => item.Id).ToArray(), "PlanRejected", () =>
                {
                    if (languagePath is not null)
                    {
                        var language = context.Language(languagePath);
                        var translated = state.Slots.Where(slot => slot.State == SessionSlotState.Translated).Select(slot => new LiteralTranslation(slot.Slot, slot.Reply!));
                        var keys = new WorldKeyConversionService(context.Inventory).Create(snapshot, translated, language, request.Get("prefix") ?? "map.");
                        var converted = keys.Select(key => key.Conversion.Slot.Address).ToHashSet();
                        operations = operations.Where(item => !converted.Contains(((LiteralPatch)item).Translation.Slot.Address)).Concat(keys).ToArray();
                    }
                    var names = literals.Where(item => WorldReferenceService.IsIdentifyingName(item.Slot.Role) && item.Reply.Text != item.Slot.SourceText).ToArray();
                    if (request.Flag("synchronize-references") && names.Length != 0)
                    {
                        var groups = new WorldReferenceService(context.Inventory).CreateGroups(snapshot, "reference-" + operation.ToString("N"), names);
                        var members = groups.SelectMany(group => group.Targets).ToHashSet();
                        operations = operations.Where(item => !members.Contains(((LiteralPatch)item).Translation.Slot.Address)).Concat(groups).ToArray();
                    }
                    return new WorldPlanBuilder(context.Inventory).Build(snapshot, operations);
                });
                var json = PatchPlanJson.Write(plan);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });
                return new(new PlanPreview(true, id, revision, operation, document.RootElement.Clone(), plan.PlanHash), Export: json);
            }
            var engine = context.Engine(); SessionOperationReceipt receipt;
            try { receipt = service.Seal(id, revision, operation, hash!, engine); }
            catch (InvalidOperationException)
            {
                var result = WriteCommands.Status(engine.ReadStatus(hash!));
                return result with { Data = new { apply = result.Data, sessionId = id, revision, operationId = operation, repair = "inspect-apply-status-and-retry-seal" } };
            }
            return new(new { completion = new { receipt.OperationId, receipt.InputHash, receipt.FromRevision, receipt.Revision, receipt.Kind, slotCount = receipt.Slots.Length },
                status = service.Status(id), planHash = hash, validationScope = "durable-session-completion" });
        }
        catch (FileNotFoundException error) when (seal)
        { throw new CliFailure("FILE_NOT_FOUND", error.Message, Repair("inspect-apply-status-and-retry-seal")); }
        catch (ReferencePolicyException error) when (seal)
        { throw new CliFailure("UNSUPPORTED_SOURCE", error.Message, Repair("rollback-and-create-new-reference-plan")); }
        catch (Exception error) when (seal && error is ArgumentException or ArtifactConflictException)
        { throw new CliFailure("CONFLICT", error.Message, Repair("inspect-session-status-and-apply-proof")); }
        catch (Exception error) when (seal && error is IOException or UnauthorizedAccessException && CliApplication.Code(error) != "RESOURCE_LIMIT")
        { throw new CliFailure("SESSION_SEAL_IO", error.Message, Repair("retry-same-seal")); }
    }
}
