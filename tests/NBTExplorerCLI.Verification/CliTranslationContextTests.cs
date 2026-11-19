using System.Collections.Immutable;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.CliScanTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class CliTranslationContextTests
{
    private const string Stat = "§c-1% damage and §a+5% speed";
    private const string Dialogue = "Friends, lower all 4 beacons before you leave. The barrier will fall, but the passage stays open at − 2% power.";

    public static void ProtectsVisibleNumbersAndFormatting()
    {
        WithWorld((world, work, session) =>
        {
            using var started = Success(Call(work, world, session, "start", "--minecraft-version", "1.8.1", "--source-language", "en_us", "--target-language", "zh_tw", "--page-size", "10"));
            using var exported = Success(Call(work, world, session, "export"));
            var data = exported.RootElement.GetProperty("data");
            var page = SessionExchangeJson.ReadPage(data.GetProperty("page").GetRawText());
            var slot = page.Slots.Single(item => item.SourceText == Stat);
            var template = SessionExchangeJson.ReadReply(data.GetProperty("replyTemplate").GetRawText());
            Invocation Submit(Hash256 id, string translation)
            {
                var reply = template with { OperationId = Guid.NewGuid(), Slots = template.Slots.Select(item => item.SlotId == id
                    ? item with { State = SessionReplyState.Translated, Text = translation } : item).ToImmutableArray() };
                var file = Path.Combine(work, "reply.json");
                File.WriteAllText(file, SessionExchangeJson.WriteReply(reply));
                return Call(work, world, session, "submit", "--reply", file);
            }
            foreach (var invalid in new[] { "§c1% 傷害 §a+5% 速度", "§c-1 傷害 §a+5% 速度", "§c-11% 傷害 §a+5% 速度",
                "§c-1% 傷害 §a+5% 速度 2", "§a-1% 傷害 §c+5% 速度", "-1% 傷害 §a+5% 速度", "§c+5% 傷害 §a-1% 速度" })
            {
                var denied = Submit(slot.SlotId, invalid);
                Require(denied.Exit == 3 && denied.Output.Contains("VisibleTextMismatch", StringComparison.Ordinal), "damage to literal values or formatting must fail before state advances");
            }
            var dialogue = page.Slots.Single(item => item.SourceText == Dialogue);
            var droppedSign = Submit(dialogue.SlotId, Dialogue.Replace("− 2%", "2%", StringComparison.Ordinal));
            Require(droppedSign.Exit == 3 && droppedSign.Output.Contains("VisibleTextMismatch", StringComparison.Ordinal), "spaced minus signs are protected");
            using var accepted = Success(Submit(slot.SlotId, "§c傷害 -1%，§a速度 +5%"));
            using var plan = Success(Call(work, world, session, "plan", "--direct", "--revision", "1"));
        });
    }

    public static void KeepsClassicSignLinesAndWholeDialogueTogether()
    {
        WithWorld((world, work, session) =>
        {
            using var started = Success(Call(work, world, session, "start", "--minecraft-version", "1.8.1", "--source-language", "en_us", "--target-language", "zh_tw", "--match-pattern", "[A-Za-z]", "--page-size", "3"));
            var data = started.RootElement.GetProperty("data");
            var count = data.GetProperty("status").GetProperty("pages").GetInt32();
            var pages = Enumerable.Range(0, count).Select(index =>
            {
                using var exported = Success(Call(work, world, session, "export", "--page", index.ToString()));
                return SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
            }).ToArray();
            var slots = pages.SelectMany(page => page.Slots).ToDictionary(slot => slot.SlotId.Hex);
            var groups = data.GetProperty("groups").EnumerateArray().Where(group => group.GetProperty("kind").GetString() == "sign.front");
            var signs = groups.Select(group => group.GetProperty("slots").EnumerateArray().Select(id => id.GetString()!).ToArray()).ToArray();
            Require(signs.Length == 2 && signs.All(group => group.Length == 2), "legacy Text1–Text4 use the sign face as ordered context");
            Require(signs.Any(group => group.Select(id => slots[id].SourceText).SequenceEqual([Stat, "Keep walking"])), "sign field order is retained");
            Require(signs.All(group => pages.Any(page => group.All(id => page.Slots.Any(slot => slot.SlotId.Hex == id)))), "a sign that fits must not be split at a page boundary");
            var firstSignPage = pages.Single(page => page.Slots.Any(slot => slot.SourceText == Stat));
            var promptFile = Path.Combine(work, "prompt.txt");
            using var withPrompt = Success(Call(work, world, session, "export", "--page", firstSignPage.Page.ToString(), "--output", promptFile));
            Require(File.ReadAllText(promptFile).Contains("sign.front", StringComparison.Ordinal)
                && File.ReadAllText(promptFile).Contains("Context groups (slot positions)", StringComparison.Ordinal),
                "the standalone prompt identifies context group boundaries without requiring a separate JSON response");
            using var ungrouped = Success(Call(work, world, session, "export", "--page", firstSignPage.Page.ToString(), "--no-group"));
            Require(!ungrouped.RootElement.GetProperty("data").GetProperty("prompt").GetString()!.Contains("Context groups (slot positions)", StringComparison.Ordinal),
                "explicitly ungrouped export omits context metadata");
            Require(slots.Values.Count(slot => slot.SourceText == Dialogue) == 1, "a whole long utterance is one slot");
            var fragments = data.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("slots").EnumerateArray()
                .Select(id => slots[id.GetString()!].SourceText).ToArray());
            Require(fragments.Any(group => group.SequenceEqual(["Watch the bridge.", "The road ahead forks."])), "structured fragments of one utterance share ordered context");
        });
    }

    private static Invocation Call(string work, string world, Guid session, string operation, params string[] args) => Run(work,
        ["translate", operation, world, "--state-root", Path.Combine(work, "state"), "--session-id", session.ToString(), .. args]);

    private static void WithWorld(Action<string, string, Guid> use)
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            static NbtEntry Text(string key, string value) => new(key, new NbtString(value));
            static NbtCompound Sign(string first, string second) => new([Text("id", "Sign"), Text("Text1", JsonSerializer.Serialize(new { text = first })),
                Text("Text2", JsonSerializer.Serialize(new { text = second }))]);
            var dialogue = new NbtCompound([Text("id", "Control"), Text("Command", "/say " + Dialogue)]);
            var fragments = new NbtCompound([Text("id", "Control"), Text("Command", "/tellraw @p {\"text\":\"Watch the bridge.\",\"extra\":[{\"text\":\"The road ahead forks.\"}]}")]);
            var tiles = new NbtList(NbtType.Compound, [Sign(Stat, "Keep walking"), Sign("Follow the river", "Find the bridge"), dialogue, fragments]);
            var level = new NbtCompound([new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)), new("TileEntities", tiles)]);
            WorldDiscoveryTests.Put(world, "region/r.0.0.mca", WorldDiscoveryTests.Region(new("", new NbtCompound([new("Level", level)])), [0]));
            WorldDiscoveryTests.Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Classic(), NbtCompression.GZip));
            use(world, work, Guid.NewGuid());
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
