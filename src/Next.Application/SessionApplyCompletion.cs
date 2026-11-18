using Next.Domain;
using Next.Formats;

namespace Next.Application;

public sealed partial class TranslationSessionService
{
    public SessionOperationReceipt Seal(Guid sessionId, long revision, Guid operationId, Hash256 planHash, WorldApplyEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return SealCore(sessionId, revision, operationId, planHash, null, () => engine).Receipt;
    }
    public (SessionOperationReceipt Receipt, SessionStatus Status) SealWithStatus(Guid sessionId, long revision, Guid operationId,
        Hash256 planHash, Guid worldId, Func<WorldApplyEngine> openEngine)
    {
        ArgumentNullException.ThrowIfNull(openEngine);
        return SealCore(sessionId, revision, operationId, planHash, worldId, openEngine);
    }
    private (SessionOperationReceipt Receipt, SessionStatus Status) SealCore(Guid sessionId, long revision, Guid operationId,
        Hash256 planHash, Guid? worldId, Func<WorldApplyEngine> openEngine)
    {
        ArgumentNullException.ThrowIfNull(planHash);
        if (operationId == Guid.Empty) throw new ArgumentException("Completion operation identity is required", nameof(operationId));
        var (state, head) = Load(sessionId);
        if (worldId is { } expected && state.Source.WorldId != expected) throw new ArtifactConflictException("Session belongs to a different world root");
        var previous = state.Operations.SingleOrDefault(operation => operation.OperationId == operationId);
        if (previous is not null)
        {
            if (previous.Kind != "apply" || previous.InputBase64 is null) throw new ArtifactConflictException("Operation identity was used by another session operation");
            var retained = SessionApplyCompletionJson.ReadUtf8(Convert.FromBase64String(previous.InputBase64));
            if (retained.Plan.PlanHash != planHash || retained.Revision != revision) throw new ArtifactConflictException("Completion identity was used with different input");
            return (previous, Status(state));
        }
        if (state.Revision != revision) throw new ArtifactConflictException("Session revision changed before completion");
        var completion = openEngine().SessionCompletion(sessionId, revision, operationId, planHash);
        var changed = SessionApplyCompletions.Apply(state, completion); var bytes = SessionApplyCompletionJson.WriteUtf8(completion);
        var receipt = new SessionOperationReceipt(operationId, Hash256.Compute(bytes), revision, checked(revision + 1), "apply", Ids(state.Slots.Select(slot => slot.Slot.Id.Digest)), Convert.ToBase64String(bytes));
        var updated = changed with { Revision = receipt.Revision, Operations = state.Operations.Add(receipt) };
        var saved = Save(updated, receipt, head, out var persisted);
        return (saved, Status(persisted));
    }
}

public sealed partial class WorldApplyEngine
{
    internal SessionApplyCompletion SessionCompletion(Guid sessionId, long revision, Guid operationId, Hash256 planHash)
    {
        var active = Load(planHash) ?? throw new FileNotFoundException("No durable apply journal exists for this plan");
        var receipt = Receipt(active);
        if (active.Journal.Direction != ApplyDirection.Forward || active.Journal.Phase != ApplyJournalPhase.Completed || receipt?.Success != true)
            throw new InvalidOperationException("The current journal must have completed its forward apply successfully");
        WorldPlanBuilder.RequireReferencePolicy(active.Header.Plan);
        return new(operationId, sessionId, revision, active.Header.Plan, active.Header.Prepared, receipt);
    }
}
