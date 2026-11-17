using System.Text.Json;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;

namespace Next.Cli;

internal static class WriteExecution
{
    internal static CliResult Apply(PreparedWorldPlan prepared, WorldApplyEngine engine, string root, string state)
        => Run(engine, () => engine.Apply(prepared, true), prepared.Plan.PlanHash, root, state, prepared.Plan.Operations.Select(item => item.Id).ToArray())!;
    internal static CliResult Recover(WorldApplyEngine engine, Hash256 hash, string root, string state, bool rollback)
        => Run(engine, () => engine.Recover(hash, true, rollback), hash, root, state, null)!;
    internal static CliResult? TryReplay(WorldApplyEngine engine, PatchPlan plan, string root, string state)
        => Run(engine, () => engine.ReadStatus(plan.PlanHash) is null ? null : engine.Recover(plan.PlanHash, true), plan.PlanHash, root, state, plan.Operations.Select(item => item.Id).ToArray());
    internal static CliResult Status(WorldApplyEngine engine, Hash256 hash, string root, string state)
        => Run(engine, null, hash, root, state, null)!;
    private static CliResult? Run(WorldApplyEngine engine, Func<ApplyReceipt?>? execute, Hash256 hash, string root, string state, IReadOnlyList<string>? operationIds)
    {
        // Reserve the longest repair before any journal I/O.
        var recovery = new { world = Path.GetFullPath(root), stateRoot = Path.GetFullPath(state), repair = "rollback-and-create-new-reference-plan" };
        if (Encoding.UTF8.GetByteCount(CommandOutputJson.Write("recover", recovery)) > 32_768)
            throw new JsonOutputLimitException("Recovery identity exceeds reserved command envelope space");
        ApplyReceipt? receipt = null;
        string? policyError = null;
        try
        {
            try { if (execute is not null) receipt = execute(); }
            catch (ReferencePolicyException error)
            {
                policyError = error.Message;
                var current = WriteCommands.Status(engine.ReadStatus(hash));
                return current with { Data = new { journal = current.Data, repair = "rollback-and-create-new-reference-plan", policyError }, Code = "UNSUPPORTED_SOURCE", Outcome = "Unsupported", Exit = 4, Export = null };
            }
            if (execute is not null && receipt is null) return null;
            var currentStatus = engine.ReadStatus(hash);
            if (currentStatus is null && receipt is null) throw new ApplyJournalMissingException();
            return WriteCommands.Status(currentStatus, receipt);
        }
        catch (Exception error) when (StateFailure(error))
        {
            var missing = error is ApplyJournalMissingException;
            recovery = new { recovery.world, recovery.stateRoot, repair = policyError is null ? "inspect-status-then-resume-same-journal" : "rollback-and-create-new-reference-plan" };
            if (receipt is null && error is AggregateException)
                try { receipt = engine.ReadReceipt(hash); } catch (Exception unavailable) when (StateFailure(unavailable)) { }
            var raw = receipt is null ? null : ApplyReceiptJson.Write(receipt);
            using var json = raw is null ? null : JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 1024 });
            return new(new { planHash = hash, receiptIsTerminal = false, receipt = json is null ? (JsonElement?)null : json.RootElement.Clone(),
                unconfirmedOperations = receipt is null && operationIds is not null ? CommandOutputJson.UnconfirmedOperations(operationIds) : null,
                recovery, journalMayBeAbsent = receipt is null, policyError, policyRepair = policyError is null ? null : "rollback-and-create-new-reference-plan",
                diagnosticCode = CliApplication.Code(error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : error) }, missing ? "FILE_NOT_FOUND" : "RECOVERY_REQUIRED", missing ? "Failed" : "RecoveryRequired", missing ? 5 : 7, raw);
        }
    }
    private static bool StateFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or JsonException
        || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(StateFailure);
}
