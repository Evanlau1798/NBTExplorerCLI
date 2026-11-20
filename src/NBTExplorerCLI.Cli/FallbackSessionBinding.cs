using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;

namespace NBTExplorerCLI.Cli;

internal sealed class FallbackSessionBinding
{
    private sealed record Target(string File, string? Entry, string? Dimension, ChunkPosition? Chunk,
        TypedPath? Node, TypedPath? Component, long? Start, long? Length);

    private readonly Dictionary<string, Hash256?> hashes;
    private readonly ILookup<Target, SessionSlot> slots;
    private readonly Dictionary<Hash256, SessionReplySlot> replies = [];
    internal TranslationSession Session { get; }
    internal IEnumerable<SessionReplySlot> Replies => replies.Values;

    internal FallbackSessionBinding(CliArguments request, string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var id) || id == Guid.Empty)
            throw new CliFailure("INVALID_ARGUMENTS", "Use a nonempty GUID for --session-id");
        var context = new WriteContext(request.Input!, request);
        Session = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(context.State, "sessions"))).Read(id);
        if (Session.Source.WorldId != context.Inventory.WorldId)
            throw new CliFailure("CONFLICT", "Fallback session belongs to another world");
        var source = TranslationLanguages.SourceOption(request.Get("source-language"));
        if (source != TranslationLanguages.Automatic && source != Session.SourceLocale
            || request.Get("target-language") is { } target && TranslationLanguages.Target(target) != Session.TargetLocale)
            throw new CliFailure("CONFLICT", "Fallback languages differ from the session");
        SessionCommands.RequireFresh(context, Session);
        hashes = Session.Source.Sources.ToDictionary(item => item.Artifact.RelativePath, item => item.ContentHash, StringComparer.Ordinal);
        slots = Session.Slots.Where(item => item.State != SessionSlotState.Applied && item.Slot.Address.JsonTarget == JsonTargetKind.Value
            && (item.Slot.Address.Syntax is null || Function(item.Slot.Address.Artifact) && item.Slot.Address.NodePath.Segments.IsEmpty))
            .ToLookup(item =>
            {
                var address = item.Slot.Address;
                return Key(address, address.Syntax?.StartByte, address.Syntax?.LengthBytes);
            });
    }

    internal void Add(QuerySource source, TextSlot draft, SnbtSpan? span, string text)
    {
        if (hashes.GetValueOrDefault(source.Artifact.RelativePath) != source.RawHash || span is not null && !Function(source.Artifact))
            throw new CliFailure("CONFLICT", "Fallback selection does not match a session source preimage");
        var target = Key(draft.Address, span?.StartByte, span?.LengthBytes);
        var matches = slots[target].Take(2).ToArray();
        if (matches is not [var match] || match.Slot.SourceText != draft.SourceText
            || match.Slot.Address.Syntax is { } syntax && syntax.PreimageHash != (source.EntryHash ?? source.RawHash).Hex)
            throw new CliFailure("CONFLICT", "Fallback leaf has no unique verified session slot");
        var slot = match.Slot;
        var validation = slot.Validate(new(slot.Id, slot.SourceTextHash, Session.TargetLocale, text));
        if (validation != SlotValidation.Valid) throw new CliFailure("CONFLICT", validation.ToString());
        if (!replies.TryAdd(slot.Id.Digest, new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, text)))
            throw new CliFailure("CONFLICT", "Multiple fallback replies target the same session slot");
    }

    private static bool Function(ArtifactAddress artifact)
        => (artifact.ContainerEntry ?? artifact.RelativePath).EndsWith(".mcfunction", StringComparison.Ordinal);

    private static Target Key(SourceAddress address, long? start, long? length) => new(address.Artifact.RelativePath,
        address.Artifact.ContainerEntry, address.Dimension, address.Chunk, start is null ? address.NodePath : null, address.ComponentPath, start, length);
}
