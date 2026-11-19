using NBTExplorerCLI.Domain;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace NBTExplorerCLI.Formats;

public static class SessionExchangeJson
{
    public const string PageFamily = "nbtcli.session-page";
    public const string ReplyFamily = "nbtcli.session-reply";
    public const int Version = 1;
    public static SessionPage SealPage(SessionPage page)
    {
        ValidatePage(page); return page with { PageId = PageDigest(page) };
    }
    private static Hash256 PageDigest(SessionPage page) => Hash256.Compute(Encoding.UTF8.GetBytes(ContractJson.Write(PageFamily, page with { PageId = Hash256.Compute([]) })));
    public static string WritePage(SessionPage page)
    {
        ValidatePage(page); var json = ContractJson.Write(PageFamily, page); _ = ReadPage(json); return json;
    }
    public static SessionPage ReadPage(string json) => ContractJson.Read(json, PageFamily, data =>
    {
        ContractJson.Fields(data, "sessionId", "revision", "snapshotId", "page", "pageId", "sourceLocale", "targetLocale", "policyVersion", "preservePlaceholderOrder", "slots");
        var slotFields = new[] { "slotId", "sourceTextHash", "sourceText", "context", "allowedEmpty", "placeholders", "format" };
        foreach (var slot in data.GetProperty("slots").EnumerateArray())
            ContractJson.Fields(slot, slot.TryGetProperty("protectVisibleText", out _) ? [.. slotFields, "protectVisibleText"] : slotFields);
        var page = ContractJson.Decode<SessionPage>(data); ValidatePage(page);
        if (PageDigest(page) != page.PageId) throw new JsonException("Session page digest differs");
        return page;
    });
    public static string WriteReply(SessionReply reply)
    {
        ValidateReply(reply); var json = ContractJson.Write(ReplyFamily, reply); _ = ReadReply(json); return json;
    }
    public static SessionReply ReadReply(string json) => ContractJson.Read(json, ReplyFamily, data =>
    {
        ContractJson.Fields(data, "operationId", "sessionId", "revision", "snapshotId", "page", "pageId", "targetLocale", "slots");
        var reply = ContractJson.Decode<SessionReply>(data); ValidateReply(reply); return reply;
    });
    public static string ReplyTemplate(SessionPage page, Guid operationId)
    {
        ValidatePage(page);
        return WriteReply(new(operationId, page.SessionId, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale,
            page.Slots.Select(slot => new SessionReplySlot(slot.SlotId, slot.SourceTextHash, SessionReplyState.Unchanged, null)).ToImmutableArray()));
    }
    public static string Prompt(SessionPage page, Guid operationId, IReadOnlyDictionary<Hash256, SessionContextGroup>? context = null)
        => "請依 sourceLocale 與 targetLocale 翻譯下方每個 slot 的 decoded text。回覆只使用提供的 JSON 協議；保留所有身分與 revision 欄位及完整 slot 集合。\n"
            + "需翻譯時將 state 設為 translated、text 設為譯文（允許空值者可為空字串）；明確不需翻譯才使用 unchanged、text=null。不得加入地址、AST、事件、樣式或操作種類。字面標記不具有替換功能。\n"
            + "format 為 percent 或 normalizedPercent 時，保留相同參數索引及次數，可用 %1$s 等索引調整語序。normalizedPercent 的數值格式會轉為字串參數。literal 只依明列的 placeholders 保護文字。\n"
            + (page.Slots.Any(slot => slot.ProtectVisibleText) ? "protectVisibleText 為 true 時，保留原數字、正負號、百分比與 § 格式碼的順序，不得加入額外標記。同一組的告示牌各行、書頁與對話片段應連同原文閱讀，回覆仍按各 slot 填寫。\n" : "")
            + (page.PolicyVersion == SessionPage.GlyphProtectionPolicy ? "受保護的私用字元用於自訂字型圖示，請依原順序保留。\n" : "")
            + PromptContext(page, context)
            + "來源頁面：\n" + WritePage(page) + "\n回覆範本：\n" + ReplyTemplate(page, operationId);
    private static string PromptContext(SessionPage page, IReadOnlyDictionary<Hash256, SessionContextGroup>? context)
    {
        if (context is null || context.Count == 0) return "";
        var groups = page.Slots.Select((slot, index) => (Group: context.GetValueOrDefault(slot.SlotId), Position: index + 1))
            .Where(item => item.Group is { Slots.Length: > 1 }).GroupBy(item => item.Group!);
        var lines = string.Join('\n', groups.Select(group => group.Key.Kind + " " + string.Join(", ", group.Select(item =>
            item.Position.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        return lines.Length == 0 ? "" : "Context groups (slot positions)\n" + lines + "\n";
    }
    private static void ValidatePage(SessionPage page)
    {
        if (page is null || page.SessionId == Guid.Empty || page.Revision < 0 || page.Page < 0 || page.SnapshotId is null || page.PageId is null
            || string.IsNullOrWhiteSpace(page.SourceLocale) || string.IsNullOrWhiteSpace(page.TargetLocale) || string.IsNullOrWhiteSpace(page.PolicyVersion)
            || page.Slots.IsDefault || page.Slots.Length is < 1 or > 100_000) throw new JsonException("Invalid session page");
        var ids = new HashSet<Hash256>();
        foreach (var slot in page.Slots)
            if (slot is null || slot.SlotId is null || !ids.Add(slot.SlotId) || slot.SourceTextHash is null || slot.SourceText is null || slot.Context is null
                || slot.SourceTextHash != TextRevision.Compute(slot.SourceText) || !Enum.IsDefined(slot.Format) || slot.Placeholders.IsDefault || slot.Placeholders.Length > 256
                || slot.Placeholders.Any(string.IsNullOrEmpty) || slot.Placeholders.Distinct(StringComparer.Ordinal).Count() != slot.Placeholders.Length) throw new JsonException("Invalid session page slot");
    }
    private static void ValidateReply(SessionReply reply)
    {
        if (reply is null || reply.OperationId == Guid.Empty || reply.SessionId == Guid.Empty || reply.Revision < 0 || reply.Page < 0 || reply.SnapshotId is null || reply.PageId is null
            || string.IsNullOrWhiteSpace(reply.TargetLocale) || reply.Slots.IsDefault || reply.Slots.Length is < 1 or > 100_000) throw new JsonException("Invalid session reply");
        var ids = new HashSet<Hash256>();
        foreach (var slot in reply.Slots)
            if (slot is null || slot.SlotId is null || !ids.Add(slot.SlotId) || slot.SourceTextHash is null || !Enum.IsDefined(slot.State)
                || slot.State == SessionReplyState.Unchanged && slot.Text is not null || slot.State == SessionReplyState.Translated && slot.Text is null) throw new JsonException("Invalid session reply slot");
    }
}
