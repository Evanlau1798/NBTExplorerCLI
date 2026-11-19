using System.Collections.Immutable;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class SessionPageProjection
{
    public static SessionPage Create(TranslationSession session, int page, long? revision = null)
        => Create(session, page, revision ?? session.Revision, session.Slots.ToDictionary(slot => slot.Slot.Id.Digest, slot => slot.Slot), session.Slots.ToDictionary(slot => slot.Slot.Id.Digest, slot => slot.Format));
    internal static SessionPage Create(TranslationSession session, int page, long revision, IReadOnlyDictionary<Hash256, TextSlot> slots, IReadOnlyDictionary<Hash256, PlaceholderFormat> formats)
    {
        if (page < 0 || page >= session.Pages.Length) throw new ArgumentOutOfRangeException(nameof(page));
        var policyVersion = SessionPage.PolicyFor(session.Pages[page].Any(id => slots[id].Placeholders.ProtectPrivateUseGlyphs));
        return SessionExchangeJson.SealPage(new(session.SessionId, revision, session.Source.SnapshotId.Digest, page, Hash256.Compute([]), session.SourceLocale, session.TargetLocale, policyVersion, session.Policy.PreservePlaceholderOrder,
            session.Pages[page].Select(id => { var slot = slots[id]; return new SessionPageSlot(id, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty,
                slot.Placeholders.Tokens.Order(StringComparer.Ordinal).ToImmutableArray(), formats.GetValueOrDefault(id), slot.Placeholders.ProtectVisibleText); }).ToImmutableArray()));
    }
}
