using System.Globalization;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class SessionWorldCommands
{
    internal static CliResult Execute(CliArguments request, Guid id, Guid operation)
    {
        if (!long.TryParse(request.Get("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) throw new CliFailure("INVALID_ARGUMENTS", "Supply an exact nonnegative --revision");
        var seal = request.Subcommand == "seal";
        if (seal && !request.Flag("operation-id")) throw new CliFailure("INVALID_ARGUMENTS", "Seal requires a retained --operation-id for exact retry");
        if (!seal && !request.Flag("direct")) throw new CliFailure("INVALID_ARGUMENTS", "Session planning currently requires --direct");
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
                var operations = state.Slots.Select(slot => (PatchOperation)new LiteralPatch("session-" + operation.ToString("N") + "-" + slot.Slot.Id.Digest.Hex,
                    new(slot.Slot, slot.Reply ?? new(slot.Slot.Id, slot.Slot.SourceTextHash, slot.Slot.TargetLocale, slot.Slot.SourceText)))).ToArray();
                CommandOutputJson.ValidateRejectionBudget(operations.Select(item => item.Id).ToArray());
                var plan = WriteCommands.BeforeCommit(operations.Select(item => item.Id).ToArray(), "PlanRejected", () => new WorldPlanBuilder(context.Inventory).Build(snapshot, operations));
                var json = PatchPlanJson.Write(plan);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });
                return new(new { preview = true, sessionId = id, revision, operationId = operation, plan = document.RootElement.Clone() }, Export: json);
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
        catch (Exception error) when (seal && error is ArgumentException or ArtifactConflictException)
        { throw new CliFailure("CONFLICT", error.Message, Repair("inspect-session-status-and-apply-proof")); }
        catch (Exception error) when (seal && error is IOException or UnauthorizedAccessException && CliApplication.Code(error) != "RESOURCE_LIMIT")
        { throw new CliFailure("SESSION_SEAL_IO", error.Message, Repair("retry-same-seal")); }
    }
}
