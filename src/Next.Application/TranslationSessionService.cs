using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Collections.Immutable;
using System.Text;

namespace Next.Application;

public sealed partial class TranslationSessionService(ITranslationSessionStore store)
{
    private readonly ITranslationSessionStore store = store ?? throw new ArgumentNullException(nameof(store));
    public SessionOperationReceipt Start(WorldQuerySnapshot snapshot, Guid sessionId, string sourceLocale, string targetLocale, Guid operationId, SessionPolicy? policy = null)
        => Start(CreateStartRequest(snapshot, sessionId, sourceLocale, targetLocale, policy), operationId);
    public static SessionStartRequest CreateStartRequest(WorldQuerySnapshot snapshot, Guid sessionId, string sourceLocale, string targetLocale, SessionPolicy? policy = null, Func<TextOccurrence, bool>? select = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot); var manifest = snapshot.Manifest;
        var source = new SessionSourceSnapshot(manifest.WorldId, manifest.SnapshotId, manifest.InputSources, manifest.Scope.Dimensions, manifest.Scope.Regions, manifest.Scope.Types, manifest.Scope.ArtifactPaths, manifest.Versions, manifest.CoverageComplete);
        var selected = snapshot.ReadSources().SelectMany(item => item.Text?.Occurrences ?? []).Where(item => select?.Invoke(item) ?? true).Take(100_001).ToArray();
        if (selected.Length > 100_000) throw new SessionSlotLimitException();
        var slots = selected.Select(item => item.ToSlot(sourceLocale, targetLocale)).ToImmutableArray();
        source = source with { ContextRoots = SessionStorageRoots.Build(snapshot, slots) };
        return new(sessionId, source, sourceLocale, targetLocale, policy ?? new(), slots);
    }
    public TranslationSession Read(Guid sessionId) => Load(sessionId).State;
    public SessionOperationReceipt Start(SessionStartRequest request, Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation identity is required", nameof(operationId));
        _ = TranslationSessionJson.WriteStart(request); request = BindFormats(request);
        var input = Hash256.Compute(Encoding.UTF8.GetBytes(TranslationSessionJson.WriteStart(request))); var previous = store.Read(request.SessionId);
        if (previous is not null) return Replay(Decode(previous), operationId, input, "start") ?? throw new ArtifactConflictException("Session already exists");
        var formats = (request.Formats.IsDefault ? [] : request.Formats).ToDictionary(item => item.SlotId, item => item.Format);
        var slots = request.Slots.Select(slot => new SessionSlot(slot, Format: formats.GetValueOrDefault(slot.Id.Digest))).ToImmutableArray();
        var receipt = new SessionOperationReceipt(operationId, input, -1, 0, "start", Ids(request.Slots.Select(slot => slot.Id.Digest)), null);
        var session = new TranslationSession(request.SessionId, 0, request.Source, request.SourceLocale, request.TargetLocale, request.Policy, slots, SessionPageBuilder.Build(request), [receipt]);
        return Save(session, receipt, null);
    }
    public SessionPage Export(Guid sessionId, int page)
    {
        var state = Read(sessionId); var result = SessionPageProjection.Create(state, page);
        if (Encoding.UTF8.GetByteCount(SessionExchangeJson.Prompt(result, Guid.NewGuid())) > state.Policy.MaximumPageBytes) throw new InvalidDataException("Stored session page exceeds its byte policy");
        return result;
    }
    public SessionOperationReceipt Submit(Guid sessionId, ReadOnlyMemory<byte> replyBytes)
    {
        if (replyBytes.Length > 67_108_864) throw new ArgumentException("Reply exceeds byte budget", nameof(replyBytes));
        var reply = SessionExchangeJson.ReadReply(new UTF8Encoding(false, true).GetString(replyBytes.Span)); var input = Hash256.Compute(replyBytes.Span);
        var (state, head) = Load(sessionId); var replay = Replay(state, reply.OperationId, input, "reply"); if (replay is not null) return replay;
        var page = SessionPageProjection.Create(state, reply.Page); var pageIds = page.Slots.Select(slot => slot.SlotId).ToHashSet();
        if (state.Slots.Any(slot => slot.State == SessionSlotState.Applied && pageIds.Contains(slot.Slot.Id.Digest))) throw new InvalidOperationException("Explicitly reset applied slots before submitting a new reply");
        var validation = SessionReplyValidator.Validate(page, state.Slots.Select(item => item.Slot), reply, Formats(state));
        if (!validation.Valid) throw new SessionReplyRejectedException(validation.Issues);
        var translated = validation.Value!.Translations.ToDictionary(item => item.Slot.Id.Digest, item => item.Reply); var unchanged = validation.Value.UnchangedSlots.ToHashSet();
        var slots = state.Slots.Select(item => translated.TryGetValue(item.Slot.Id.Digest, out var value) ? item with { State = SessionSlotState.Translated, Reply = value }
            : unchanged.Contains(item.Slot.Id.Digest) ? item with { State = SessionSlotState.Unchanged, Reply = null } : item).ToImmutableArray();
        var receipt = new SessionOperationReceipt(reply.OperationId, input, state.Revision, checked(state.Revision + 1), "reply", Ids(reply.Slots.Select(item => item.SlotId)), Convert.ToBase64String(replyBytes.Span));
        return Save(state with { Revision = receipt.Revision, Slots = slots, Operations = state.Operations.Add(receipt) }, receipt, head);
    }
    public SessionReplyValidation ValidatedPage(Guid sessionId, int page)
    {
        var state = Read(sessionId); var expected = SessionPageProjection.Create(state, page); var slots = state.Slots.ToDictionary(item => item.Slot.Id.Digest);
        var replies = expected.Slots.Where(item => slots[item.SlotId].State is SessionSlotState.Translated or SessionSlotState.Unchanged)
            .Select(item => { var slot = slots[item.SlotId]; return new SessionReplySlot(item.SlotId, item.SourceTextHash, slot.State == SessionSlotState.Translated ? SessionReplyState.Translated : SessionReplyState.Unchanged, slot.Reply?.Text); }).ToImmutableArray();
        return SessionReplyValidator.Validate(expected, state.Slots.Select(item => item.Slot), new(Guid.NewGuid(), sessionId, state.Revision, state.Source.SnapshotId.Digest, page, expected.PageId, state.TargetLocale, replies), Formats(state));
    }
    private static IReadOnlyDictionary<Hash256, PlaceholderFormat> Formats(TranslationSession state) => state.Slots.ToDictionary(item => item.Slot.Id.Digest, item => item.Format);
    private static SessionStartRequest BindFormats(SessionStartRequest request)
    {
        var declared = (request.Formats.IsDefault ? [] : request.Formats).ToDictionary(item => item.SlotId, item => item.Format);
        var formats = ImmutableArray.CreateBuilder<SessionSlotFormat>();
        foreach (var slot in request.Slots)
        {
            var known = MinecraftPlaceholderProfiles.ForRole(slot.Role); var selected = declared.GetValueOrDefault(slot.Id.Digest, known);
            if (known != PlaceholderFormat.Literal && selected != known) throw new ArgumentException("Known source formatting grammar cannot be overridden", nameof(request));
            if (selected != PlaceholderFormat.Literal) formats.Add(new(slot.Id.Digest, selected));
        }
        return request with { Formats = formats.ToImmutable() };
    }
    private static ImmutableArray<Hash256> Ids(IEnumerable<Hash256> ids) => ids.OrderBy(id => id.Hex, StringComparer.Ordinal).ToImmutableArray();
    private (TranslationSession State, Hash256 Head) Load(Guid id)
    {
        var bytes = store.Read(id) ?? throw new KeyNotFoundException("Session does not exist"); var state = Decode(bytes);
        if (state.SessionId != id) throw new InvalidDataException("Session store identity differs"); return (state, Hash256.Compute(bytes));
    }
    private static TranslationSession Decode(byte[] bytes) => TranslationSessionJson.Read(new UTF8Encoding(false, true).GetString(bytes));
    private static SessionOperationReceipt? Replay(TranslationSession state, Guid operation, Hash256 input, string kind)
    {
        var receipt = state.Operations.SingleOrDefault(item => item.OperationId == operation);
        if (receipt is not null && (receipt.InputHash != input || receipt.Kind != kind)) throw new ArtifactConflictException("Operation identity was already used with different input");
        return receipt;
    }
    private SessionOperationReceipt Save(TranslationSession state, SessionOperationReceipt receipt, Hash256? expected)
    {
        var bytes = Encoding.UTF8.GetBytes(TranslationSessionJson.Write(state));
        try { store.Write(state.SessionId, bytes, expected); }
        catch (ArtifactConflictException) { return Replay(Read(state.SessionId), receipt.OperationId, receipt.InputHash, receipt.Kind) ?? throw new ArtifactConflictException("Session revision changed"); }
        return receipt;
    }
}

public sealed class SessionReplyRejectedException(ImmutableArray<SessionValidationIssue> issues) : Exception("Session reply failed validation")
{
    public ImmutableArray<SessionValidationIssue> Issues { get; } = issues;
}

public sealed class SessionSlotLimitException() : Exception("Selected session exceeds the 100000-slot budget");
