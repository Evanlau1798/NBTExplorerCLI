using Next.Domain;

namespace Next.Application;

internal static class LegacySlotMapping
{
    internal static bool TryBossbar(LegacyRecord record, out TypedPath path, out string context)
    {
        path = TypedPath.Root; context = "";
        const string prefix = "level.dat#Data/CustomBossEvents/"; const string suffix = "/Name";
        if (record.Type != "bossbar" || record.LineCount != 1 || record.Dimension is not null || record.Path is not { } legacy
            || !legacy.StartsWith(prefix, StringComparison.Ordinal) || !legacy.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var key = legacy[prefix.Length..^suffix.Length];
        if (key.Length == 0 || key.Contains('/') || key.Contains('\\') || key.Contains('#') || key is "." or ".." || key.StartsWith('[')) return false;
        path = new([PathSegment.ForKey("Data"), PathSegment.ForKey("CustomBossEvents"), PathSegment.ForKey(key), PathSegment.ForKey("Name")]);
        context = "Boss 血條 (" + key + ")"; return record.Context == context;
    }
    internal static bool Validate(TextSlot slot, string text, Hash256 snapshot)
    {
        var session = Guid.NewGuid(); var pageId = Hash256.Compute([]);
        var page = new SessionPage(session, 0, snapshot, 0, pageId, slot.SourceLocale, slot.TargetLocale, "literal-v1", false,
            [new(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens)]);
        return SessionReplyValidator.Validate(page, [slot], new(Guid.NewGuid(), session, 0, snapshot, 0, pageId, slot.TargetLocale,
            [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, text)])).Valid;
    }
}
