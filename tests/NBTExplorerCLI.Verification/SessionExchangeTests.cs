using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionExchangeTests
{
    internal static readonly Guid SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    internal static TextSlot[] Slots() => [Slot("literal {{T0}} | ‖§‖ \\ \" \n\0 %s"), new(Address(new([PathSegment.ForKey("second")])), "Second", "book.page", "en_us", "zh_tw", true, PlaceholderPolicy.None)];
    internal static SessionPage Page(TextSlot[] slots, bool ordered = false) => new(SessionId, 2, Hash256.Compute([1]), 0, Hash256.Compute([2]), "en_us", "zh_tw", "literal-v1", ordered,
        slots.Select(slot => new SessionPageSlot(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens)).ToImmutableArray());
    internal static SessionReply Reply(SessionPage page) => new(Guid.NewGuid(), page.SessionId, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale,
        [new(page.Slots[0].SlotId, page.Slots[0].SourceTextHash, SessionReplyState.Translated, "字面 {{T1}} | ‖§‖ \\ \" \n\0 %s"), new(page.Slots[1].SlotId, page.Slots[1].SourceTextHash, SessionReplyState.Translated, "")]);
    public static void ExactDecodedTextAndDistinctEmptyStates()
    {
        var slots = Slots(); var page = Page(slots); var reply = Reply(page);
        var result = SessionReplyValidator.Validate(page, slots, reply);
        Require(result.Valid && result.Value!.Translations.Select(item => item.Reply.Text).SequenceEqual(reply.Slots.Select(item => item.Text)), "literal markers, controls and allowed empty translations are exact decoded text, never replacement templates");
        var unchanged = reply with { Slots = reply.Slots.SetItem(1, reply.Slots[1] with { State = SessionReplyState.Unchanged, Text = null }) };
        var other = SessionReplyValidator.Validate(page, slots, unchanged);
        Require(other.Valid && other.Value!.Translations.Length == 1 && other.Value.UnchangedSlots.SequenceEqual(new[] { slots[1].Id.Digest }), "unchanged is explicit and distinct from translated empty string");
        Require(!SessionReplyValidator.Validate(page, slots, unchanged with { Slots = unchanged.Slots.SetItem(1, unchanged.Slots[1] with { Text = "ignored" }) }).Valid, "unchanged cannot carry discarded translated data");
    }
    public static void ExactMembershipAndBinding()
    {
        var slots = Slots(); var page = Page(slots); var reply = Reply(page);
        SessionReply[] invalid = [reply with { Slots = [reply.Slots[0]] }, reply with { Slots = [reply.Slots[0], reply.Slots[0]] }, reply with { Slots = reply.Slots.Add(reply.Slots[0] with { SlotId = Hash256.Compute([99]) }) },
            reply with { Revision = 1 }, reply with { SessionId = Guid.NewGuid() }, reply with { SnapshotId = Hash256.Compute([3]) }, reply with { Page = 1 }, reply with { PageId = Hash256.Compute([3]) }, reply with { TargetLocale = "en_us" },
            reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { SourceTextHash = Hash256.Compute([4]) }) }, reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { Text = "lost placeholder" }) },
            reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { Text = "" }) }, reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { State = (SessionReplyState)99 }) }];
        Require(invalid.All(value => !SessionReplyValidator.Validate(page, slots, value).Valid), "missing/extra/duplicate slot, stale revision/source, foreign session/snapshot/page/locale and invalid values reject the entire page");
        Require(SessionReplyValidator.Validate(page, slots, reply with { Slots = reply.Slots.Reverse().ToImmutableArray() }).Valid, "reply array order does not choose source pairing; exact slot IDs do");
    }
    public static void PlaceholderOrderPolicy()
    {
        var slot = new TextSlot(Address(TypedPath.Root), "One %s two {name}", "format", "en_us", "zh_tw", false, new(["%s", "{name}"]));
        var page = Page([slot]); var reply = new SessionReply(Guid.NewGuid(), page.SessionId, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale, [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, "{name} %s")]);
        Require(SessionReplyValidator.Validate(page, [slot], reply).Valid, "accepted G01 literal placeholder contract still allows reordering");
        Require(!SessionReplyValidator.Validate(page with { PreservePlaceholderOrder = true }, [slot], reply).Valid, "explicit ordered-format policy checks placeholder order using the same validator");
        Require(SessionReplyValidator.Validate(page with { PreservePlaceholderOrder = true }, [slot], reply with { Slots = [reply.Slots[0] with { Text = "%s 與 {name}" }] }).Valid, "ordered positive control retains declared syntax and token sequence");
    }
    public static void PlaceholderCapacityIsAnExplicitOutcome()
    {
        var text = string.Concat(Enumerable.Repeat("%s", 100_001));
        var slot = new TextSlot(Address(TypedPath.Root), text, "format", "en_us", "zh_tw", false, new(["%s"])); var page = Page([slot], true);
        var reply = new SessionReply(Guid.NewGuid(), page.SessionId, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale, [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, text)]);
        var result = SessionReplyValidator.Validate(page, [slot], reply);
        Require(!result.Valid && result.Issues.Single().Code == "PlaceholderLimit", "an ordered contract above its capacity reports a limit instead of suggesting incorrect token order");
        CandidateIoTests.Reject<PlaceholderLimitException>(() => PercentPlaceholders.Equivalent(text, text));
    }
}
