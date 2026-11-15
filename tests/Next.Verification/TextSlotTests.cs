using Next.Domain;

namespace Next.Verification;

internal static class TextSlotTests
{
    internal static readonly Guid World = Guid.Parse("11111111-2222-3333-4444-555555555555");
    internal static SourceAddress Address(TypedPath path, string file = "level.dat")
        => new(new(World, file, ArtifactKind.Nbt), path, "item.name");
    internal static TextSlot Slot(string text = "Hello %s", bool empty = false)
        => new(Address(new([PathSegment.ForKey("text")])), text, "item.display", "en_us", "zh_tw", empty,
            new(["%s"]));
    internal static SlotReply Reply(TextSlot slot, string text) => new(slot.Id, slot.SourceTextHash, slot.TargetLocale, text);

    public static void TextAndRevision()
    {
        var source = "literal {{T0}} ‖§‖ | \" \\ \n\0\ud800 %s";
        var slot = Slot(source);
        var revised = Slot(source + "!");
        Require(slot.SourceText == source && slot.Context == "item.display" && slot.Role == "item.name",
            "原文與語境必須原樣保留，不能嵌私有markers或先escape");
        Require(slot.Id == revised.Id && slot.SourceTextHash != revised.SourceTextHash,
            "同位置文字變動只改revision hash，不能改slot ID");
        Require(Slot("\ud800").SourceTextHash != Slot("\ufffd").SourceTextHash, "hash必須保存surrogate code units");
        Require(slot.Validate(Reply(slot, source)) == SlotValidation.Valid, "所有字元都可為合法decoded文字");
        (string Text, string Digest)[] vectors =
        [
            ("Hello %s", "e96dd057ebff49fbe88f0484ee916a4f3df674fc8dafb60520b37f577128922a"),
            ("\ud800", "87ede1dfd4bc7641e9f809caa12eb878a226cfab5468ae048c262aa01076d826"),
            (new string('x', 512) + "\ud800" + new string('y', 512), "5f547456a20ded98157dbb3226ae89daf1cbbbafb97fc0d102ed31ab9e8e117a")
        ];
        foreach (var (text, digest) in vectors)
            Require(TextRevision.Compute(text).Hex == digest, "text revision需符合獨立Python UTF-16BE/hashlib向量，包含buffer邊界");
    }

    public static void ReplyBoundaries()
    {
        var slot = Slot();
        var reply = Reply(slot, "你好 %s");
        Require(slot.Validate(reply) == SlotValidation.Valid, "正確回覆應通過");
        Require(slot.Validate(reply with { SlotId = Slot("other").Id }) == SlotValidation.Valid, "同位置slotId與revision分離");
        var elsewhere = new TextSlot(Address(TypedPath.Root), "Hello %s", "item.display", "en_us", "zh_tw", false, new(["%s"]));
        Require(slot.Validate(reply with { SlotId = elsewhere.Id }) == SlotValidation.WrongSlot, "不可套用其他位置的回覆");
        Require(slot.Validate(reply with { SourceTextHash = Slot("new").SourceTextHash }) == SlotValidation.StaleSource,
            "過期來源hash必須拒絕");
        Require(slot.Validate(reply with { TargetLocale = "en_us" }) == SlotValidation.WrongLocale, "locale需精確相符");
        Require(slot.Validate(reply with { Text = "" }) == SlotValidation.EmptyDisallowed, "空值需明示授權");
        Require(slot.Validate(reply with { Text = "你好" }) == SlotValidation.PlaceholderMismatch, "不可遺失placeholder");
        Require(slot.Validate(reply with { Text = "%s %s" }) == SlotValidation.PlaceholderMismatch, "不可額外複製placeholder");
        var empty = new TextSlot(Address(TypedPath.Root), "Hello", "name", "en_us", "zh_tw", true, PlaceholderPolicy.None);
        Require(empty.Validate(Reply(empty, "")) == SlotValidation.Valid, "明示允許空值時應保留空字串");
    }

    public static void PlaceholderContracts()
    {
        var tokens = new List<string> { "%s", "{name}" };
        var policy = new PlaceholderPolicy(tokens);
        tokens.Clear();
        Require(policy.Tokens.Length == 2 && policy.Accepts("%s %s {name}", "{name} %s\n%s"), "明示token保留次數，允許重排");
        Require(!policy.Accepts("%s %s {name}", "%s {name}"), "重複token不能遺失");
        Require(PlaceholderPolicy.None.Accepts("{{T0}} ‖§‖", "正常譯文"), "未宣告的marker只是普通文字");
        Reject(() => new PlaceholderPolicy([""]));
        Reject(() => new PlaceholderPolicy(["%s", "%s"]));
        Reject(() => new PlaceholderPolicy([null!]));
        Reject(() => new TextSlot(Address(TypedPath.Root), "source", "context", "", "zh_tw", false, policy));
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("非法契約應拒絕");
    }
}
