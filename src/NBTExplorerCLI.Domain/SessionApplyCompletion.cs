using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public sealed record SessionApplyCompletion(Guid OperationId, Guid SessionId, long Revision, PatchPlan Plan, PreparedManifest Prepared, ApplyReceipt Receipt);

public static class SessionApplyCompletions
{
    public static TranslationSession Apply(TranslationSession state, SessionApplyCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(completion);
        var plan = completion.Plan; var receipt = completion.Receipt;
        if (completion.OperationId == Guid.Empty || completion.SessionId != state.SessionId || completion.Revision != state.Revision
            || plan.WorldId != state.Source.WorldId || plan.SnapshotId != state.Source.SnapshotId || receipt.PlanHash != plan.PlanHash)
            throw new ArgumentException("Session completion differs from its exact source and revision");
        if (!receipt.Success) throw new InvalidOperationException("Every promised output must complete before session sealing");
        _ = completion.Prepared.Receipt(plan, receipt.Revision, receipt.Results, receipt.DependenciesComplete);
        if (state.Slots.Any(slot => slot.State is not (SessionSlotState.Translated or SessionSlotState.Unchanged)))
            throw new ArgumentException("All session replies must be validated and unapplied before sealing");
        var edits = plan.Operations.SelectMany(operation => operation switch
        {
            LiteralPatch literal => new[] { (literal.Translation.Slot.Address, literal.Translation.Slot.SourceText, literal.Translation.Reply.Text) },
            KeyConversionPatch key => [(key.Conversion.Slot.Address, key.Conversion.Slot.SourceText, key.Conversion.Reply.Text)],
            ReferencePatch reference => reference.Synchronization.Members.Select(member => (member.Address, member.ExpectedText, member.ReplacementText)),
            _ => Enumerable.Empty<(SourceAddress, string, string)>()
        }).ToDictionary(edit => edit.Item1, edit => (Before: edit.Item2, After: edit.Item3));
        foreach (var item in state.Slots)
        {
            var found = edits.TryGetValue(item.Slot.Address, out var edit);
            var expected = item.State == SessionSlotState.Translated ? item.Reply!.Text : item.Slot.SourceText;
            if (item.State == SessionSlotState.Translated && !found || found && (edit.Before != item.Slot.SourceText || edit.After != expected))
                throw new ArgumentException("A session translation is missing or differs from the completed plan");
        }
        return state with { Slots = state.Slots.Select(slot => slot with { State = SessionSlotState.Applied }).ToImmutableArray() };
    }
}
