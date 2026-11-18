using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public enum SessionReplyState { Translated, Unchanged }
public enum PlaceholderFormat { Literal, Percent, NormalizedPercent }
public sealed record SessionPageSlot(Hash256 SlotId, Hash256 SourceTextHash, string SourceText, string Context, bool AllowedEmpty, ImmutableArray<string> Placeholders, PlaceholderFormat Format = PlaceholderFormat.Literal);
public sealed record SessionPage(Guid SessionId, long Revision, Hash256 SnapshotId, int Page, Hash256 PageId,
    string SourceLocale, string TargetLocale, string PolicyVersion, bool PreservePlaceholderOrder, ImmutableArray<SessionPageSlot> Slots);
public sealed record SessionReplySlot(Hash256 SlotId, Hash256 SourceTextHash, SessionReplyState State, string? Text);
public sealed record SessionReply(Guid OperationId, Guid SessionId, long Revision, Hash256 SnapshotId, int Page, Hash256 PageId,
    string TargetLocale, ImmutableArray<SessionReplySlot> Slots);
public sealed record SessionValidationIssue(Hash256? SlotId, string Code);
public sealed record SessionReplyValidation(ImmutableArray<SessionValidationIssue> Issues, ValidatedSessionReply? Value)
{
    public bool Valid => Issues.IsEmpty && Value is not null;
}
public sealed class ValidatedSessionReply
{
    public ImmutableArray<LiteralTranslation> Translations { get; }
    public ImmutableArray<Hash256> UnchangedSlots { get; }
    internal ValidatedSessionReply(ImmutableArray<LiteralTranslation> translations, ImmutableArray<Hash256> unchanged)
        => (Translations, UnchangedSlots) = (translations, unchanged);
}
