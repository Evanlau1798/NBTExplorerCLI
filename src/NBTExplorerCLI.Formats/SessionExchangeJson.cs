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
        => "Translate each decoded slot from sourceLocale to targetLocale. Infer the source from the text if it is und. Return the provided JSON shape with all identities, revision fields and slots.\n"
            + "Use state translated with the translation in text; empty text is allowed only when permitted. Use unchanged and text=null to keep the source. Leave addresses, AST, events, styles and operation types out of the reply.\n"
            + "For percent and normalizedPercent, retain argument indices and counts; use indices such as %1$s to reorder them. normalizedPercent converts numeric formats to string parameters. For literal, preserve listed placeholders.\n"
            + (page.Slots.Any(slot => slot.ProtectVisibleText) ? "When protectVisibleText is true, preserve numbers, signs, percent signs and section-sign formatting codes in order. Read grouped sign lines, book pages and dialogue together, and reply per slot.\n" : "")
            + (page.PolicyVersion == SessionPage.GlyphProtectionPolicy ? "Preserve protected private-use font glyphs in order.\n" : "")
            + PromptContext(page, context)
            + "Source page\n" + WritePage(page) + "\nReply template\n" + ReplyTemplate(page, operationId);
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
