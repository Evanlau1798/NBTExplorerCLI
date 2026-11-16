using System.Collections.Immutable;

namespace Next.Domain;

public enum SessionCommandKind { Jump, Note, Glossary, Reset }
public sealed record SessionNote(Hash256? SlotId, string Text, string Origin, bool Confirmed);
public sealed record SessionGlossaryEntry(string Source, string Translation, string Origin, bool Confirmed);
public sealed record SessionWorkspace(int CurrentPage, ImmutableArray<SessionNote> Notes, ImmutableArray<SessionGlossaryEntry> Glossary)
{
    public static SessionWorkspace Empty { get; } = new(0, [], []);
}
public sealed record SessionCommand(Guid OperationId, Guid SessionId, long Revision, SessionCommandKind Kind,
    int? Page = null, Hash256? SlotId = null, string? Text = null, string? Source = null, string? Origin = null, bool Confirmed = false, ImmutableArray<Hash256> ResetSlots = default);
public sealed record SessionStatus(Guid SessionId, long Revision, int CurrentPage, int Pages, int Total, int Pending, int Translated, int Unchanged, int Conflict, int Applied);
public sealed record SessionResetPreview(long Revision, ImmutableArray<Hash256> Slots, int TranslationsRetainedInHistory);
public sealed record SessionContextGroup(string Kind, SourceAddress Container, StableIdentity GroupId, StableIdentity MemberDigest, ImmutableArray<Hash256> Slots);
public sealed record TranslationMemoryCandidate(StableIdentity Key, Guid SessionId, Hash256 SlotId, long Revision, string Text, string Provenance);

public static class SessionWorkspaceCommands
{
    public static TranslationSession Apply(TranslationSession state, SessionCommand command)
    {
        if (command is null || command.OperationId == Guid.Empty || command.SessionId != state.SessionId || command.Revision != state.Revision || !Enum.IsDefined(command.Kind))
            throw new ArgumentException("Invalid session command binding", nameof(command));
        var workspace = state.Workspace ?? SessionWorkspace.Empty; var slots = state.Slots;
        switch (command.Kind)
        {
            case SessionCommandKind.Jump:
                if (command.Page is null || command.Page < 0 || command.Page >= state.Pages.Length || command.SlotId is not null || command.Text is not null || command.Source is not null || command.Origin is not null || command.Confirmed || !command.ResetSlots.IsDefaultOrEmpty) Invalid();
                workspace = workspace with { CurrentPage = command.Page!.Value }; break;
            case SessionCommandKind.Note:
                if (command.Page is not null || command.Source is not null || command.Text is null || string.IsNullOrWhiteSpace(command.Origin) || command.Text.Length > 16_384 || command.Origin.Length > 1024
                    || !command.ResetSlots.IsDefaultOrEmpty || command.SlotId is not null && !slots.Any(slot => slot.Slot.Id.Digest == command.SlotId) || workspace.Notes.Length >= 10_000) Invalid();
                workspace = workspace with { Notes = workspace.Notes.Add(new(command.SlotId, command.Text!, command.Origin!, command.Confirmed)) }; break;
            case SessionCommandKind.Glossary:
                if (command.Page is not null || command.SlotId is not null || command.Source is null || command.Text is null || string.IsNullOrWhiteSpace(command.Origin)
                    || command.Source.Length > 16_384 || command.Text.Length > 16_384 || command.Origin.Length > 1024 || !command.ResetSlots.IsDefaultOrEmpty || workspace.Glossary.Length >= 10_000) Invalid();
                workspace = workspace with { Glossary = workspace.Glossary.Add(new(command.Source!, command.Text!, command.Origin!, command.Confirmed)) }; break;
            case SessionCommandKind.Reset:
                if (command.Page is not null || command.SlotId is not null || command.Text is not null || command.Source is not null || command.Origin is not null || !command.Confirmed
                    || command.ResetSlots.IsDefaultOrEmpty || command.ResetSlots.Length > 100_000 || command.ResetSlots.Any(id => id is null) || command.ResetSlots.Distinct().Count() != command.ResetSlots.Length) Invalid();
                var reset = command.ResetSlots.ToHashSet();
                var known = slots.Select(slot => slot.Slot.Id.Digest).ToHashSet();
                if (reset.Any(id => !known.Contains(id))) Invalid();
                slots = slots.Select(slot => reset.Contains(slot.Slot.Id.Digest) ? slot with { State = SessionSlotState.Pending, Reply = null, Reason = null } : slot).ToImmutableArray(); break;
        }
        return state with { Workspace = workspace, Slots = slots };
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new ArgumentException("Invalid or oversized session command");
}
