using System.Text.Json;
using System.Text.Json.Nodes;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.SessionExchangeTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class SessionExchangeJsonTests
{
    public static void ProtocolAndTemplateUseOneSchema()
    {
        var slots = Slots(); var page = SessionExchangeJson.SealPage(Page(slots)); var json = SessionExchangeJson.WritePage(page);
        var roundtrip = SessionExchangeJson.ReadPage(json);
        Require(SessionExchangeJson.WritePage(roundtrip) == json && roundtrip.Slots[0].SourceText == slots[0].SourceText, "versioned page preserves exact source text and its ordered digest binding");
        Require(!json.Contains("level.dat", StringComparison.Ordinal) && !json.Contains("nodePath", StringComparison.Ordinal) && !json.Contains("canonical", StringComparison.Ordinal), "exchange only exposes digest identities and decoded text, not canonical address bytes or AST authority");
        var operation = Guid.NewGuid(); var template = SessionExchangeJson.ReplyTemplate(page, operation); var reply = SessionExchangeJson.ReadReply(template);
        Require(reply.OperationId == operation && SessionReplyValidator.Validate(page, slots, reply).Valid && reply.Slots.All(slot => slot.State == SessionReplyState.Unchanged && slot.Text is null), "generated reply template is parsed by the actual import schema and explicitly represents unchanged slots");
        Require(SessionExchangeJson.Prompt(page, operation).Contains(template, StringComparison.Ordinal), "export prompt embeds the same generated schema rather than a separately maintained parser contract");
        var edited = Reply(page) with { Slots = Reply(page).Slots.SetItem(0, Reply(page).Slots[0] with { Text = "你好\ud800 %s" }) };
        Require(SessionExchangeJson.ReadReply(SessionExchangeJson.WriteReply(edited)).Slots[0].Text == "你好\ud800 %s", "lossless UTF16 reply text uses the shared contract string encoding");
        var changed = JsonNode.Parse(json)!; changed["data"]!["slots"]![0]!["sourceText"] = "changed";
        Reject<JsonException>(() => SessionExchangeJson.ReadPage(changed.ToJsonString()));
    }
    public static void UnknownAndProtectedFieldsAreRejected()
    {
        var page = SessionExchangeJson.SealPage(Page(Slots())); var reply = SessionExchangeJson.WriteReply(Reply(page));
        Reject<NotSupportedException>(() => SessionExchangeJson.ReadReply(reply.Replace("\"schemaVersion\":1", "\"schemaVersion\":99", StringComparison.Ordinal)));
        var ast = JsonNode.Parse(reply)!; ast["data"]!["slots"]![0]!["text"] = JsonNode.Parse("{\"text\":\"你好\",\"clickEvent\":{\"action\":\"run_command\",\"value\":\"/say changed\"}}");
        Reject<JsonException>(() => SessionExchangeJson.ReadReply(ast.ToJsonString()));
        foreach (var field in new[] { "patchKind", "address", "style", "event", "uuid" })
        {
            var modified = JsonNode.Parse(reply)!; modified["data"]!["slots"]![0]![field] = "injected";
            Reject<JsonException>(() => SessionExchangeJson.ReadReply(modified.ToJsonString()));
        }
        var missing = JsonNode.Parse(reply)!; missing["data"]!.AsObject().Remove("snapshotId");
        Reject<JsonException>(() => SessionExchangeJson.ReadReply(missing.ToJsonString()));
        var duplicate = JsonNode.Parse(reply)!; duplicate["data"]!["slots"]!.AsArray().Add(duplicate["data"]!["slots"]![0]!.DeepClone());
        Reject<JsonException>(() => SessionExchangeJson.ReadReply(duplicate.ToJsonString()));
        Reject<JsonException>(() => SessionExchangeJson.ReadReply(reply.Replace("\"text\":", "\"text\":\"ignored\",\"text\":", StringComparison.Ordinal)));
    }
}
