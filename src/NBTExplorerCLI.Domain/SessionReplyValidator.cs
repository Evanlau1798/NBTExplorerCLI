using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public static class SessionReplyValidator
{
    public static SessionReplyValidation Validate(SessionPage page, IEnumerable<TextSlot> localSlots, SessionReply reply, IReadOnlyDictionary<Hash256, PlaceholderFormat>? formats = null)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(localSlots); ArgumentNullException.ThrowIfNull(reply);
        var issues = ImmutableArray.CreateBuilder<SessionValidationIssue>();
        SessionReplyValidation Invalid(string code) => new([new(null, code)], null);
        if (page.SessionId == Guid.Empty || page.Revision < 0 || page.Page < 0 || page.SnapshotId is null || page.PageId is null
            || string.IsNullOrWhiteSpace(page.SourceLocale) || string.IsNullOrWhiteSpace(page.TargetLocale) || string.IsNullOrWhiteSpace(page.PolicyVersion)
            || page.Slots.IsDefault || page.Slots.Length is < 1 or > 100_000) return Invalid("InvalidPage");
        var local = localSlots.Take(100_001).ToArray();
        if (local.Length > 100_000 || local.Any(slot => slot is null) || local.Select(slot => slot.Id.Digest).Distinct().Count() != local.Length) return Invalid("InvalidLocalSlots");
        var byId = local.ToDictionary(slot => slot.Id.Digest);
        var expected = new Dictionary<Hash256, TextSlot>();
        foreach (var item in page.Slots)
        {
            if (item is null || item.SlotId is null || !byId.TryGetValue(item.SlotId, out var slot) || !expected.TryAdd(item.SlotId, slot)
                || item.SourceTextHash != slot.SourceTextHash || item.SourceText != slot.SourceText || item.Context != slot.Context || item.AllowedEmpty != slot.AllowedEmpty
                || item.Placeholders.IsDefault || !item.Placeholders.Order(StringComparer.Ordinal).SequenceEqual(slot.Placeholders.Tokens.Order(StringComparer.Ordinal))
                || item.ProtectVisibleText != slot.Placeholders.ProtectVisibleText
                || !Enum.IsDefined(item.Format) || item.Format != (formats?.GetValueOrDefault(item.SlotId) ?? PlaceholderFormat.Literal)
                || slot.SourceLocale != page.SourceLocale || slot.TargetLocale != page.TargetLocale) return Invalid("LocalSlotMismatch");
        }
        if (reply.OperationId == Guid.Empty || reply.Slots.IsDefault || reply.Slots.Length > 100_000) return Invalid("InvalidReply");
        if (reply.SessionId != page.SessionId) return Invalid("WrongSession");
        if (reply.Revision != page.Revision) return Invalid("StaleRevision");
        if (reply.SnapshotId != page.SnapshotId) return Invalid("WrongSnapshot");
        if (reply.Page != page.Page || reply.PageId != page.PageId) return Invalid("WrongPage");
        if (reply.TargetLocale != page.TargetLocale) return Invalid("WrongLocale");
        var seen = new HashSet<Hash256>(); var translations = ImmutableArray.CreateBuilder<LiteralTranslation>(); var unchanged = ImmutableArray.CreateBuilder<Hash256>(); long textBytes = 0;
        foreach (var item in reply.Slots)
        {
            if (item is null || item.SlotId is null) return Invalid("InvalidReplySlot");
            if (!seen.Add(item.SlotId)) { issues.Add(new(item.SlotId, "DuplicateSlot")); continue; }
            if (!expected.TryGetValue(item.SlotId, out var slot)) { issues.Add(new(item.SlotId, "UnexpectedSlot")); continue; }
            if (item.SourceTextHash != slot.SourceTextHash) { issues.Add(new(item.SlotId, "StaleSource")); continue; }
            if (!Enum.IsDefined(item.State) || item.State == SessionReplyState.Unchanged && item.Text is not null || item.State == SessionReplyState.Translated && item.Text is null)
            { issues.Add(new(item.SlotId, "InvalidReplyState")); continue; }
            if (item.State == SessionReplyState.Unchanged) { unchanged.Add(item.SlotId); continue; }
            textBytes += (long)item.Text!.Length * 2;
            if (textBytes > 67_108_864) return Invalid("ReplyTextLimit");
            var value = new SlotReply(slot.Id, item.SourceTextHash, reply.TargetLocale, item.Text);
            var validation = slot.Validate(value);
            if (validation != SlotValidation.Valid) { issues.Add(new(item.SlotId, validation.ToString())); continue; }
            var format = formats?.GetValueOrDefault(item.SlotId) ?? PlaceholderFormat.Literal;
            try
            {
                if (format != PlaceholderFormat.Literal && !PercentPlaceholders.Equivalent(slot.SourceText, item.Text, format == PlaceholderFormat.NormalizedPercent))
                { issues.Add(new(item.SlotId, "PlaceholderArgumentMismatch")); continue; }
                if (page.PreservePlaceholderOrder && !SameOrder(slot.SourceText, item.Text, slot.Placeholders.Tokens))
                { issues.Add(new(item.SlotId, "PlaceholderOrderMismatch")); continue; }
            }
            catch (PlaceholderLimitException) { issues.Add(new(item.SlotId, "PlaceholderLimit")); continue; }
            translations.Add(new(slot, value));
        }
        foreach (var missing in expected.Keys.Where(id => !seen.Contains(id))) issues.Add(new(missing, "MissingSlot"));
        return issues.Count == 0 ? new([], new(translations.ToImmutable(), unchanged.ToImmutable())) : new(issues.ToImmutable(), null);
    }
    private static bool SameOrder(string source, string target, ImmutableArray<string> tokens)
    {
        using var left = Sequence(source, tokens).GetEnumerator(); using var right = Sequence(target, tokens).GetEnumerator();
        var count = 0;
        while (left.MoveNext())
        {
            if (++count > 100_000) throw new PlaceholderLimitException();
            if (!right.MoveNext() || left.Current != right.Current) return false;
        }
        return !right.MoveNext();
    }
    private static IEnumerable<string> Sequence(string text, ImmutableArray<string> tokens)
    {
        var queue = new PriorityQueue<(string Token, int Index, int Order), (int Index, int Order)>();
        foreach (var (token, order) in tokens.Order(StringComparer.Ordinal).Select((token, order) => (token, order)))
        {
            var index = text.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0) queue.Enqueue((token, index, order), (index, order));
        }
        while (queue.TryDequeue(out var match, out _))
        {
            yield return match.Token;
            var followingIndex = text.IndexOf(match.Token, match.Index + 1, StringComparison.Ordinal);
            if (followingIndex >= 0) queue.Enqueue((match.Token, followingIndex, match.Order), (followingIndex, match.Order));
        }
    }
}
