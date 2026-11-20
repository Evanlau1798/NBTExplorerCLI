using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;

namespace NBTExplorerCLI.Application;

public sealed partial class TranslationSessionService
{
    public SessionStatus Status(Guid id) => Status(Read(id));
    public static SessionStatus Status(TranslationSession state)
    {
        ArgumentNullException.ThrowIfNull(state); var counts = state.Slots.GroupBy(item => item.State).ToDictionary(group => group.Key, group => group.Count());
        return new(state.SessionId, state.Revision, state.Workspace?.CurrentPage ?? 0, state.Pages.Length, state.Slots.Length, counts.GetValueOrDefault(SessionSlotState.Pending), counts.GetValueOrDefault(SessionSlotState.Translated), counts.GetValueOrDefault(SessionSlotState.Unchanged), counts.GetValueOrDefault(SessionSlotState.Conflict), counts.GetValueOrDefault(SessionSlotState.Applied));
    }
    public SessionSlot Get(Guid id, Hash256 slotId) => Get(Read(id), slotId);
    public static SessionSlot Get(TranslationSession state, Hash256 slotId)
        => state.Slots.SingleOrDefault(slot => slot.Slot.Id.Digest == slotId) ?? throw new KeyNotFoundException("Unknown session slot");
    public SessionPage Advance(Guid id)
    {
        var state = Read(id); var pending = state.Slots.Where(slot => slot.State is SessionSlotState.Pending or SessionSlotState.Conflict).Select(slot => slot.Slot.Id.Digest).ToHashSet();
        var start = state.Workspace?.CurrentPage ?? 0;
        for (var offset = 0; offset < state.Pages.Length; offset++)
        {
            var page = (start + offset) % state.Pages.Length;
            if (state.Pages[page].Any(pending.Contains)) return SessionPageProjection.Create(state, page);
        }
        throw new InvalidOperationException("Session has no pending pages");
    }
    public SessionOperationReceipt Execute(SessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command); var bytes = Encoding.UTF8.GetBytes(SessionCommandJson.Write(command)); var input = Hash256.Compute(bytes);
        command = SessionCommandJson.Read(new UTF8Encoding(false, true).GetString(bytes));
        var (state, head) = Load(command.SessionId); var replay = Replay(state, command.OperationId, input, "command"); if (replay is not null) return replay;
        if (command.Revision != state.Revision) throw new ArtifactConflictException("Session command revision changed");
        var updated = SessionWorkspaceCommands.Apply(state, command);
        var receipt = new SessionOperationReceipt(command.OperationId, input, state.Revision, checked(state.Revision + 1), "command", Ids(command.Kind == SessionCommandKind.Reset ? command.ResetSlots : []), Convert.ToBase64String(bytes));
        return Save(updated with { Revision = receipt.Revision, Operations = state.Operations.Add(receipt) }, receipt, head);
    }
    public SessionResetPreview PreviewReset(Guid id, IEnumerable<Hash256> slotIds)
    {
        ArgumentNullException.ThrowIfNull(slotIds); var state = Read(id); var requested = slotIds.Take(100_001).ToArray(); var known = state.Slots.ToDictionary(slot => slot.Slot.Id.Digest);
        if (requested.Length is < 1 or > 100_000 || requested.Any(slot => slot is null || !known.ContainsKey(slot)) || requested.Distinct().Count() != requested.Length) throw new ArgumentException("Invalid reset slot scope", nameof(slotIds));
        return new(state.Revision, Ids(requested), requested.Count(slot => known[slot].Reply is not null));
    }
    public ImmutableArray<SessionContextGroup> Groups(Guid id) => Groups(Read(id));
    public static ImmutableArray<SessionContextGroup> Groups(TranslationSession state)
        => SessionContextGrouping.Build(state.Slots.Select(item => item.Slot), state.Source.ContextRoots);
    public ImmutableArray<TranslationMemoryCandidate> Memory(Guid targetSession, Hash256 slotId, IEnumerable<Guid> sourceSessions, string glossaryVersion)
    {
        ArgumentNullException.ThrowIfNull(sourceSessions); ArgumentException.ThrowIfNullOrWhiteSpace(glossaryVersion);
        var target = Read(targetSession); var slot = target.Slots.SingleOrDefault(item => item.Slot.Id.Digest == slotId) ?? throw new KeyNotFoundException("Unknown target slot");
        if (target.SourceLocale == "und") return [];
        var sessions = sourceSessions.Distinct().Take(257).ToArray();
        if (sessions.Length > 256 || sessions.Any(id => id == Guid.Empty)) throw new ArgumentException("Invalid or oversized memory candidate scope", nameof(sourceSessions));
        var result = ImmutableArray.CreateBuilder<TranslationMemoryCandidate>();
        var key = MemoryKey(target, slot, glossaryVersion);
        foreach (var id in sessions)
        {
            var source = Read(id);
            foreach (var candidate in source.Slots.Where(item => item.State is SessionSlotState.Translated or SessionSlotState.Applied && item.Reply is not null))
                if (MemoryKey(source, candidate, glossaryVersion) == key)
                {
                    if (result.Count >= 100_000) throw new InvalidDataException("Memory candidate budget exceeded");
                    result.Add(new(key, id, candidate.Slot.Id.Digest, source.Revision, candidate.Reply!.Text, "validated-session:" + id.ToString("D") + ":" + candidate.Slot.Id.Digest.Hex));
                }
        }
        return result.ToImmutable();
    }
    private static StableIdentity MemoryKey(TranslationSession state, SessionSlot slot, string glossaryVersion)
    {
        var tokens = string.Concat(slot.Slot.Placeholders.Tokens.Order(StringComparer.Ordinal).Select(token => token.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + token));
        var placeholderPolicy = slot.Format + ":" + state.Policy.PreservePlaceholderOrder + ":" + TextRevision.Compute(tokens).Hex;
        if (slot.Slot.Placeholders.ProtectPrivateUseGlyphs) placeholderPolicy += ":visible-text-glyphs";
        else if (slot.Slot.Placeholders.ProtectVisibleText) placeholderPolicy += ":visible-text";
        return SourceIdentity.Memory(slot.Slot.SourceText, state.SourceLocale, state.TargetLocale, slot.Slot.Context, placeholderPolicy, glossaryVersion, state.Source.Versions.Profile);
    }
}
