using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Next.Domain;
using Next.Infrastructure;
using Next.Formats;
using static Next.Verification.CliScanTests;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CliSessionTests
{
    private static Invocation Call(string work, string world, Guid id, string action, params string[] args)
        => Run(work, ["translate", action, world, "--session-id", id.ToString(), "--state-root", Path.Combine(work, "state"), .. args]);
    private static JsonDocument Start(string work, string world, Guid id, params string[] args)
        => Success(Call(work, world, id, "start", ["--source-language", "en_us", "--target-language", "zh_tw", .. (args.Contains("--types") ? Array.Empty<string>() : ["--types", "McFunction"]), .. args]));
    private static string FileInput(string work, string name, string json)
    { var file = Path.Combine(work, name); File.WriteAllText(file, json, new UTF8Encoding(false)); return file; }
    private static SessionReply Reply(JsonDocument page) => SessionExchangeJson.ReadReply(page.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText());
    public static void FileExchangePersistsRepliesBeforeReturningTheNextPage()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            var before = File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0)));
            using var started = Start(work, world, id, "--page-size", "2");
            Require(started.RootElement.GetProperty("data").GetProperty("status").GetProperty("total").GetInt32() == 3, "start exposes every occurrence as a separate slot");
            var prompt = Path.Combine(work, "prompt.txt"); using var exported = Success(Call(work, world, id, "export", "--page", "0", "--output", prompt));
            var page = SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
            Require(page.SourceLocale == "en_us" && page.TargetLocale == "zh_tw" && page.Slots.Length == 2
                && File.ReadAllText(prompt).TrimEnd('\r', '\n') == exported.RootElement.GetProperty("data").GetProperty("prompt").GetString(), "page and human prompt are distinct exports with complete schema identities");
            var reply = Reply(exported); reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = SessionReplyState.Translated, Text = "譯文🙂" }).ToImmutableArray() };
            var file = FileInput(work, "reply.json", SessionExchangeJson.WriteReply(reply));
            using var next = Success(Call(work, world, id, "next", "--reply", file));
            var revision = next.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64();
            Require(next.RootElement.GetProperty("data").GetProperty("status").GetProperty("translated").GetInt32() == 2
                && next.RootElement.GetProperty("data").GetProperty("nextPage").GetProperty("data").GetProperty("page").GetInt32() == 1, "accepted reply is durable before the next pending page is returned");
            using var replay = Success(Call(work, world, id, "next", "--reply", file));
            using var status = Success(Call(work, world, id, "status"));
            Require(status.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64() == revision
                && replay.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64() == revision, "process restart and exact resend do not count twice");
            using var get = Success(Call(work, world, id, "get", "--slot-id", reply.Slots[0].SlotId.Hex));
            var projected = get.RootElement.GetProperty("data").GetProperty("slot");
            Require(projected.TryGetProperty("slotId", out var publicId) && publicId.ValueKind == JsonValueKind.String && !projected.TryGetProperty("slot", out _)
                && !projected.TryGetProperty("address", out _), "slot lookup exposes opaque IDs and text, with canonical local write addresses retained locally");
            Require(projected.GetProperty("reply").GetProperty("text").GetString() == "譯文🙂"
                && File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0))).SequenceEqual(before), "local validated replies remain separate from world writes");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void InvalidAndStaleRepliesPreserveWorkEvenWhenTheCacheIsFresh()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            using var started = Start(work, world, id); using var page = Success(Call(work, world, id, "export")); var reply = Reply(page);
            foreach (var invalid in new[] { reply with { Slots = reply.Slots.RemoveAt(0) }, reply with { Revision = reply.Revision + 1 } })
            {
                var result = Call(work, world, id, "submit", "--reply", FileInput(work, "bad.json", SessionExchangeJson.WriteReply(invalid)));
                Require(result.Exit == 3, "missing and stale-revision reply sets are conflicts: " + result.Output);
            }
            var duplicate = JsonNode.Parse(SessionExchangeJson.WriteReply(reply))!; var duplicateSlots = duplicate["data"]!["slots"]!.AsArray(); duplicateSlots.Add(duplicateSlots[0]!.DeepClone());
            Require(Call(work, world, id, "submit", "--reply", FileInput(work, "duplicate.json", duplicate.ToJsonString())).Exit == 5, "duplicate slot identities are rejected by the versioned reply codec");
            var path = Path.Combine(world, WorldQueryTests.Function(0)); File.WriteAllText(path, File.ReadAllText(path).Replace("Text0", "Other", StringComparison.Ordinal), new UTF8Encoding(false));
            using var refreshed = Success(Run(work, "scan", world, "--types", "McFunction", "--cache-root", Path.Combine(work, "state", "cache")));
            using var warm = Success(Run(work, "scan", world, "--types", "McFunction", "--cache-root", Path.Combine(work, "state", "cache")));
            Require(warm.RootElement.GetProperty("data").GetProperty("telemetry").GetProperty("manifestHits").GetInt32() == 1, "the changed world now has a verified warm cache");
            var stale = Call(work, world, id, "submit", "--reply", FileInput(work, "reply.json", SessionExchangeJson.WriteReply(reply))); using var rejected = JsonDocument.Parse(stale.Output);
            Require(stale.Exit == 3 && rejected.RootElement.GetProperty("data").GetProperty("repair").GetString() == "start-new-session", "a fresh cache cannot make an older session snapshot current");
            using var get = Success(Call(work, world, id, "get", "--slot-id", reply.Slots[0].SlotId.Hex));
            Require(get.RootElement.GetProperty("data").GetProperty("slot").GetProperty("state").GetString() == "pending", "invalid imports preserve the original session work");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ManagementCommandsUseRevisionCasAndRetainResetHistory()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            using var started = Start(work, world, id, "--page-size", "1"); using var page = Success(Call(work, world, id, "export")); var reply = Reply(page);
            var command = new SessionCommand(Guid.NewGuid(), id, 0, SessionCommandKind.Note, SlotId: reply.Slots[0].SlotId, Text: "附註🙂", Origin: "user", Confirmed: true, ResetSlots: []);
            var file = FileInput(work, "command.json", SessionCommandJson.Write(command)); using var note = Success(Call(work, world, id, "note", "--request", file));
            using var replay = Success(Call(work, world, id, "note", "--request", file));
            using var listed = Success(Call(work, world, id, "note"));
            Require(listed.RootElement.GetProperty("data").GetProperty("notes")[0].GetProperty("text").GetString() == "附註🙂", "note survives restart and exact resend");
            var stale = command with { OperationId = Guid.NewGuid() }; Require(Call(work, world, id, "note", "--request", FileInput(work, "stale.json", SessionCommandJson.Write(stale))).Exit == 3, "new command with old revision cannot overwrite work");
            var jump = new SessionCommand(Guid.NewGuid(), id, 1, SessionCommandKind.Jump, Page: 2, ResetSlots: []);
            using var moved = Success(Call(work, world, id, "jump", "--request", FileInput(work, "jump.json", SessionCommandJson.Write(jump))));
            var glossary = new SessionCommand(Guid.NewGuid(), id, 2, SessionCommandKind.Glossary, Text: "衛兵", Source: "Guard", Origin: "user", Confirmed: true, ResetSlots: []);
            using var term = Success(Call(work, world, id, "glossary", "--request", FileInput(work, "glossary.json", SessionCommandJson.Write(glossary))));
            using var terms = Success(Call(work, world, id, "glossary")); Require(terms.RootElement.GetProperty("data").GetProperty("glossary")[0].GetProperty("translation").GetString() == "衛兵", "glossary is durable metadata");
            using var current = Success(Call(work, world, id, "export", "--page", "0")); reply = Reply(current) with { Slots = Reply(current).Slots.Select(slot => slot with { State = SessionReplyState.Translated, Text = "已翻譯" }).ToImmutableArray() };
            using var submitted = Success(Call(work, world, id, "submit", "--reply", FileInput(work, "reply.json", SessionExchangeJson.WriteReply(reply))));
            var reset = new SessionCommand(Guid.NewGuid(), id, 4, SessionCommandKind.Reset, Confirmed: true, ResetSlots: [reply.Slots[0].SlotId]); var resetFile = FileInput(work, "reset.json", SessionCommandJson.Write(reset));
            using var preview = Success(Call(work, world, id, "reset", "--request", resetFile));
            Require(preview.RootElement.GetProperty("data").GetProperty("preview").GetProperty("translationsRetainedInHistory").GetInt32() == 1, "reset previews the exact scope without changing progress");
            using var resetResult = Success(Call(work, world, id, "reset", "--request", resetFile, "--confirm"));
            using var status = Success(Call(work, world, id, "status")); Require(status.RootElement.GetProperty("data").GetProperty("status").GetProperty("pending").GetInt32() == 3, "confirmed scoped reset retains historical reply and restores pending state");
            using var groups = Success(Call(work, world, id, "groups")); Require(groups.RootElement.GetProperty("data").GetProperty("groups").GetArrayLength() > 0, "context groups are publicly available");
            var group = groups.RootElement.GetProperty("data").GetProperty("groups")[0];
            Require(group.GetProperty("groupId").ValueKind == JsonValueKind.String && !group.TryGetProperty("container", out _), "group projections do not export physical addresses or canonical identity bytes");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void SelectionHelpAndRejectedExportsNeverWriteTheWorld()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            using var help = Success(Run(work, "translate", "help"));
            Require(help.RootElement.GetProperty("data").GetProperty("subcommands").EnumerateArray().Any(item => item.GetString() == "start"), "root help exposes implemented subcommands");
            using var started = Start(work, world, id, "--match-pattern", "^Text[01]$", "--types", "McFunction", "--standalone", "--no-group");
            Require(started.RootElement.GetProperty("data").GetProperty("status").GetProperty("total").GetInt32() == 2, "pattern/type selection retains exact independent occurrences");
            using var exported = Success(Call(work, world, id, "export"));
            Require(exported.RootElement.GetProperty("data").GetProperty("groups").GetArrayLength() == 0, "start's no-group display preference survives another process without changing the exchange page");
            var destination = Path.Combine(work, "existing.txt"); File.WriteAllText(destination, "keep");
            Require(Call(work, world, id, "export", "--output", destination).Exit == 3 && File.ReadAllText(destination) == "keep", "export preserves existing files");
            Require(Call(work, world, id, "export", "--output", Path.Combine(world, "prompt.txt")).Exit == 2 && !File.Exists(Path.Combine(world, "prompt.txt")), "tool exports cannot be placed in world inputs");
            Require(Call(work, world, id, "unknown").Exit == 2 && Call(work, world, id, "status", "--page-size", "0").Exit == 2, "unknown actions and context-invalid options fail explicitly");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void LimitsAndUnreadableSourcesKeepTheirActualClassification()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var failures = new List<string>();
        try
        {
            var oversizedId = Guid.NewGuid(); var lockedId = Guid.NewGuid();
            var oversized = Call(work, world, oversizedId, "start", "--source-language", "en_us", "--target-language", "zh_tw", "--types", "McFunction", "--page-bytes", "1024");
            if (oversized.Exit != 6) failures.Add("page budget exit=" + oversized.Exit);
            using (var locked = File.Open(Path.Combine(world, WorldQueryTests.Function(0)), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var unreadable = Call(work, world, lockedId, "start", "--source-language", "en_us", "--target-language", "zh_tw", "--types", "McFunction");
                using var error = JsonDocument.Parse(unreadable.Output);
                if (unreadable.Exit != 5 || error.RootElement.GetProperty("error").GetProperty("code").GetString() != "READ_ERROR") failures.Add("locked source exit=" + unreadable.Exit);
            }
            var store = new FileSystemTranslationSessionStore(Path.Combine(work, "state", "sessions"));
            Require(store.Read(oversizedId) is null && store.Read(lockedId) is null, "rejected starts never publish a session");
            Require(failures.Count == 0, "session error classification: " + string.Join(" | ", failures));
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MemoryCandidatesUseOnlyValidatedLocalSessionReplies()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var source = Guid.NewGuid(); var target = Guid.NewGuid();
        try
        {
            using var started = Start(work, world, source); using var page = Success(Call(work, world, source, "export")); var reply = Reply(page);
            reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = SessionReplyState.Translated, Text = "可靠譯文" }).ToImmutableArray() };
            using var submitted = Success(Call(work, world, source, "submit", "--reply", FileInput(work, "reply.json", SessionExchangeJson.WriteReply(reply))));
            using var targetStart = Start(work, world, target); using var targetPage = Success(Call(work, world, target, "export")); var targetReply = Reply(targetPage);
            using var memory = Success(Call(work, world, target, "memory", "--slot-id", targetReply.Slots[0].SlotId.Hex, "--source-sessions", source.ToString(), "--glossary-version", "1", "--limit", "1"));
            var candidate = memory.RootElement.GetProperty("data").GetProperty("candidates")[0];
            Require(candidate.GetProperty("key").ValueKind == JsonValueKind.String && candidate.GetProperty("text").GetString() == "可靠譯文" && candidate.GetProperty("provenance").GetString()!.StartsWith("validated-session:", StringComparison.Ordinal), "memory projects an opaque key and exact validated provenance without implicitly importing it");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void InvalidStartArgumentsDoNotCreateToolRepositories()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var failures = new List<string>();
        try
        {
            foreach (var invalid in new[] { new[] { "--page-size", "0" }, new[] { "--page-bytes", "100000000" }, new[] { "--match-pattern", "(" }, new[] { "--operation-id", "invalid" } })
            {
                var state = Path.Combine(work, Guid.NewGuid().ToString("N"));
                var result = Run(work, ["translate", "start", world, "--session-id", Guid.NewGuid().ToString(), "--source-language", "en_us", "--target-language", "zh_tw", "--state-root", state, .. invalid]);
                if (result.Exit != 2 || Directory.Exists(state)) failures.Add(invalid[0] + ":exit=" + result.Exit + ", stateCreated=" + Directory.Exists(state));
            }
            Require(failures.Count == 0, "invalid start arguments reject before cache/session publication: " + string.Join(" | ", failures));
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void StartReplayBindsTheRetainedGroupPresentationPreference()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid(); var operation = Guid.NewGuid().ToString();
        try
        {
            using var start = Start(work, world, id, "--operation-id", operation, "--no-group");
            using var replay = Start(work, world, id, "--operation-id", operation, "--no-group");
            Require(replay.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64() == 0, "exact start replay does not change session revision");
            var changed = Call(work, world, id, "start", "--source-language", "en_us", "--target-language", "zh_tw", "--types", "McFunction", "--operation-id", operation);
            Require(changed.Exit == 3, "same start identity cannot silently change the retained group display policy");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void PreferenceFailuresRetainAcceptedWorkAndDoNotConsumeReplies()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid(); var operation = Guid.NewGuid().ToString();
        try
        {
            var preferences = Path.Combine(work, "state", "session-preferences"); Directory.CreateDirectory(preferences);
            var marker = Path.Combine(preferences, "user.txt"); File.WriteAllText(marker, "preserve");
            var failed = Call(work, world, id, "start", "--source-language", "en_us", "--target-language", "zh_tw", "--types", "McFunction", "--operation-id", operation, "--no-group");
            using var failure = JsonDocument.Parse(failed.Output); var data = failure.RootElement.GetProperty("data");
            Require(failed.Exit == 5 && data.GetProperty("sessionId").GetGuid() == id && data.GetProperty("receipt").GetProperty("revision").GetInt64() == 0
                && data.GetProperty("repair").GetString() == "retry-start-with-same-identities" && File.ReadAllText(marker) == "preserve", "an unowned preference directory does not hide the already accepted start or overwrite user files");
            File.Delete(marker);
            using var replay = Start(work, world, id, "--operation-id", operation, "--no-group");
            using var exported = Success(Call(work, world, id, "export")); var reply = Reply(exported);
            var replyFile = FileInput(work, "reply.json", SessionExchangeJson.WriteReply(reply)); var stored = Path.Combine(preferences, "session-" + id.ToString("N") + ".json");
            using (var locked = File.Open(stored, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Require(Call(work, world, id, "submit", "--reply", replyFile).Exit == 5, "unreadable group preferences fail before consuming a reply");
                var state = new FileSystemTranslationSessionStore(Path.Combine(work, "state", "sessions"));
                Require(TranslationSessionJson.Read(Encoding.UTF8.GetString(state.Read(id)!)).Revision == 0, "presentation failure precedes reply publication");
                using var accepted = Success(Call(work, world, id, "submit", "--reply", replyFile, "--no-group"));
                Require(accepted.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64() == 1, "explicit appendix suppression permits a normal validated import despite the display-file lock");
            }
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
