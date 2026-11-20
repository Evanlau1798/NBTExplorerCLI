using System.Text;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Verification.CliScanTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class CliFallbackBatchTests
{
    internal static void KeepSelectionsAndRepliesBound()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            const string notes = "data/guide.json";
            const string function = "datapacks/sample/data/demo/functions/read.mcfunction";
            var json = """{"text":"Stock +15%","color":"green"}"""u8.ToArray();
            var command = """data modify storage demo:display lines append value '{"text":"Continue","bold":true}'"""u8.ToArray();
            Put(world, notes, json);
            Put(world, function, command);
            var first = Item(notes, json, "json");
            var second = Item(function, command, "snbt");
            second["options"]!["line"] = "1";
            var rows = new JsonArray(first, second);
            var request = new JsonObject { ["schemaFamily"] = "nbtcli.translation-draft-batch", ["schemaVersion"] = 1, ["selections"] = rows };
            var file = Path.Combine(work, "batch.json");
            var args = new[] { "translate", "fallback", world, "--batch", file, "--source-language", "en-US", "--target-language", "de-DE" };
            var baseline = RunBatch();
            var drafts = baseline["data"]!["drafts"]!.AsArray();
            Require(drafts.Count == 2 && drafts[0]!["candidates"]!.AsArray().Count == 1 && drafts[1]!["candidates"]!.AsArray().Count == 1
                && drafts[0]!["candidates"]![0]!["text"]!.GetValue<string>() == "Stock +15%"
                && drafts[1]!["candidates"]![0]!["text"]!.GetValue<string>() == "Continue", "batch returns both exact input selections and context");
            first["reply"] = drafts[0]!["replyTemplate"]!.DeepClone();
            second["reply"] = drafts[1]!["replyTemplate"]!.DeepClone();
            first["reply"]!["slots"]![0]!["text"] = "Vorrat +15%";
            second["reply"]!["slots"]![0]!["text"] = "Weiter";
            var submitted = RunBatch()["data"]!["drafts"]!;
            Require(submitted[0]!["content"]!.GetValue<string>().Contains("Vorrat +15%", StringComparison.Ordinal)
                && submitted[1]!["content"]!.GetValue<string>().Contains("Weiter", StringComparison.Ordinal), "batch validates replies through the same structured rewrite");
            Require(File.ReadAllBytes(Path.Combine(world, notes)).SequenceEqual(json)
                && File.ReadAllBytes(Path.Combine(world, function)).SequenceEqual(command), "batch remains a read-only draft workflow");

            first["reply"] = second["reply"]!.DeepClone();
            _ = RunBatch(expected: 3);
            first.Remove("reply"); second.Remove("reply");
            first["sourceHash"] = Hash256.Compute("changed"u8).Hex;
            _ = RunBatch(expected: 3);
            first["sourceHash"] = Hash256.Compute(json).Hex;
            second["options"]!["path"] = "../escape.mcfunction";
            _ = RunBatch(expected: 2);

            JsonNode RunBatch(int expected = 0)
            {
                File.WriteAllText(file, request.ToJsonString());
                var run = Run(work, args);
                Require(run.Exit == expected, "batch command returns the expected exit and enforces source binding: " + run.Output);
                return JsonNode.Parse(run.Output)!;
            }
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }

    internal static void BindDraftLeavesToExactSessionReplies()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var function = WorldQueryTests.Function(0);
            const string prefix = "\uFEFF# Maßstab\r\ntellraw @s ";
            const string component = """{"text":"Willkommen","color":"green"}""";
            var original = Encoding.UTF8.GetBytes(prefix + component + "\r\ntellraw @s " + component + "\r\n");
            Put(world, function, original);
            var state = Path.Combine(work, "state"); var id = Guid.NewGuid();
            var binding = new[] { world, "--session-id", id.ToString(), "--state-root", state };
            _ = Read("start", ["translate", "start", .. binding, "--path", function, "--source-language", "de-DE", "--target-language", "es-ES"]);
            var first = Item(function, original, "json");
            first["options"]!["byte-range"] = $"{Encoding.UTF8.GetByteCount(prefix)},{Encoding.UTF8.GetByteCount(component)}";
            var second = Item(function, original, "snbt");
            second["options"]!["line"] = "3";
            second["options"]!["text-pointer"] = "/text";
            var file = Path.Combine(work, "batch.json");
            var batch = new JsonObject { ["schemaFamily"] = "nbtcli.translation-draft-batch", ["schemaVersion"] = 1,
                ["selections"] = new JsonArray(first, second) };
            var initial = RunBatch();
            first["reply"] = initial["drafts"]![0]!["replyTemplate"]!.DeepClone();
            second["reply"] = initial["drafts"]![1]!["replyTemplate"]!.DeepClone();
            first["reply"]!["slots"]![0]!["text"] = "Bienvenida";
            second["reply"]!["slots"]![0]!["text"] = "Bienvenidos";
            var verified = RunBatch();
            var matched = verified["sessionReplySlots"]!.AsArray();
            Require(matched.Count == 2 && Text(matched[0], "slotId") != Text(matched[1], "slotId"), "identical text on two lines binds to different exact session slots");
            Require((Read("status", ["translate", "status", .. binding])["status"]?["revision"]?.GetValue<long>()) == 0, "the draft does not advance the session");
            var selections = batch["selections"]!.AsArray();
            selections[1] = first.DeepClone();
            _ = RunBatch(expected: 3);
            selections[1] = second;
            _ = RunBatch(expected: 3, "--target-language", "it-IT");

            var exported = Read("export", ["translate", "export", .. binding]);
            exported["replyTemplate"]!["data"]!["slots"] = matched.DeepClone();
            var replyFile = Path.Combine(work, "reply.json");
            File.WriteAllText(replyFile, exported["replyTemplate"]!.ToJsonString());
            var submitted = Read("submit", ["translate", "submit", .. binding, "--reply", replyFile]);
            var revision = submitted["status"]!["revision"]!.ToString();
            var plan = Path.Combine(work, "plan.json");
            _ = Read("plan", ["translate", "plan", .. binding, "--direct", "--revision", revision, "--output", plan]);
            var record = Read("apply", ["apply", world, "--state-root", state, "--world-offline", "--plan", plan]);
            var translated = File.ReadAllText(Path.Combine(world, function)).Split("\r\n");
            Require(translated[1].Contains("Bienvenida", StringComparison.Ordinal) && translated[2].Contains("Bienvenidos", StringComparison.Ordinal),
                "the existing session writer changes each bound location and preserves CRLF");
            _ = Read("recover", ["recover", world, "--state-root", state, "--world-offline", "--rollback", "--plan-hash", Text(record, "planHash")], 8);
            Require(File.ReadAllBytes(Path.Combine(world, function)).SequenceEqual(original), "the existing durable path restores every byte");
            var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("green", "aqua", StringComparison.Ordinal));
            Put(world, function, changed);
            first["sourceHash"] = Hash256.Compute(changed).Hex;
            second["sourceHash"] = Hash256.Compute(changed).Hex;
            _ = RunBatch(expected: 3);

            JsonNode RunBatch(int expected = 0, params string[] options)
            {
                File.WriteAllText(file, batch.ToJsonString());
                return Read("fallback", ["translate", "fallback", .. binding, "--batch", file, .. options], expected);
            }
            JsonNode Read(string action, string[] args, int expected = 0)
            {
                var invoked = Run(work, args);
                Require(invoked.Exit == expected, action + " returned an unexpected exit: " + invoked.Output);
                return JsonNode.Parse(invoked.Output)!["data"]!;
            }
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }

    private static string Text(JsonNode? value, string key) => value![key]!.GetValue<string>();

    private static JsonObject Item(string path, byte[] bytes, string format) => new()
    {
        ["sourceHash"] = Hash256.Compute(bytes).Hex,
        ["options"] = new JsonObject { ["path"] = path, ["format"] = format }
    };
}
