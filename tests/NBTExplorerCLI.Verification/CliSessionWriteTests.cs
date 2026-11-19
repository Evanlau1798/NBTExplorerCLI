using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class CliSessionWriteTests
{
    private static string State(string work) => Path.Combine(work, "state");
    private static Invocation Call(string work, string world, Guid id, string action, params string[] args) => Run(work,
        ["translate", action, world, "--session-id", id.ToString(), "--state-root", State(work), .. args]);
    private static void Start(string work, string world, Guid id)
    { using var started = Success(Call(work, world, id, "start", "--source-language", "en_us", "--target-language", "zh_tw")); }
    private static long Submit(string work, string world, Guid id, bool unchanged = false, Func<SessionPageSlot, string>? translate = null)
    {
        using var exported = Success(Call(work, world, id, "export")); var page = SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
        var reply = SessionExchangeJson.ReadReply(exported.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText());
        reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = unchanged ? SessionReplyState.Unchanged : SessionReplyState.Translated,
            Text = unchanged ? null : (translate ?? (item => "譯文:" + item.SourceText))(page.Slots.Single(item => item.SlotId == slot.SlotId)) }).ToImmutableArray() };
        var file = Path.Combine(work, "reply.json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply), new UTF8Encoding(false));
        using var submitted = Success(Call(work, world, id, "submit", "--reply", file));
        return submitted.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64();
    }
    private static (PatchPlan Plan, string File) Plan(string work, string world, Guid id, long revision)
    {
        var file = Path.Combine(work, "plan-" + Guid.NewGuid().ToString("N") + ".json"); var operation = Guid.NewGuid();
        using var preview = Success(Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--operation-id", operation.ToString(), "--output", file));
        Require(preview.RootElement.GetProperty("data").GetProperty("preview").GetBoolean(), "session planning is an explicit preview");
        return (PatchPlanJson.Read(File.ReadAllText(file)), file);
    }
    private static Invocation Apply(string work, string world, string plan) => Run(work, "apply", world, "--plan", plan, "--world-offline", "--state-root", State(work));
    private static Invocation Seal(string work, string world, Guid id, long revision, Guid operation, Hash256 hash) => Call(work, world, id, "seal",
        "--revision", revision.ToString(), "--operation-id", operation.ToString(), "--plan-hash", hash.Hex);
    private static FileSystemTranslationSessionStore Store(string work) => new(Path.Combine(State(work), "sessions"));
    public static void Java1201ScansAndRecoversTaggedPackText()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
            const string pack = "datapacks/p/";
            const string function = pack + "data/p/functions/main.mcfunction";
            const string recipe = pack + "data/p/recipes/output.json";
            var original = Utf8("give @s minecraft:stick{display:{Name:'{\"text\":\"Guide\"}'}} 1\r\n");
            var recipeBytes = Utf8("{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[{\"item\":\"minecraft:stone\"}],\"result\":{\"item\":\"minecraft:stick\"}}");
            WorldDiscoveryTests.Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            WorldDiscoveryTests.Put(world, pack + "pack.mcmeta", Utf8("{\"pack\":{\"pack_format\":15,\"description\":\"\"}}"));
            WorldDiscoveryTests.Put(world, function, original);
            WorldDiscoveryTests.Put(world, recipe, recipeBytes);
            WorldDiscoveryTests.Put(world, "entities/r.0.0.mca", []);
            using var scan = Success(Run(work, "scan", world, "--no-cache", "--summary"));
            Require(scan.RootElement.GetProperty("data").GetProperty("coverageComplete").GetBoolean(), "format 15 scans under the Java 1.20.1 profile");
            Start(work, world, id);
            var revision = Submit(work, world, id, translate: slot => slot.SourceText == "Guide" ? "指南" : slot.SourceText);
            var (plan, file) = Plan(work, world, id, revision);
            using var applied = Success(Apply(work, world, file));
            var updated = File.ReadAllText(Path.Combine(world, function));
            Require(updated == "give @s minecraft:stick{display:{Name:\"{\\\"text\\\":\\\"指南\\\"}\"}} 1\r\n"
                && File.ReadAllBytes(Path.Combine(world, recipe)).SequenceEqual(recipeBytes), "only the authorized NBT text inside the command changes: " + updated);
            var rollback = Run(work, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", State(work));
            Require(rollback.Exit == 8 && File.ReadAllBytes(Path.Combine(world, function)).SequenceEqual(original), "the verified preimage is restored");
            Require(new FileInfo(Path.Combine(world, "entities/r.0.0.mca")).Length == 0, "empty region artifacts are inventoried without being rewritten");
            WorldDiscoveryTests.Put(world, "entities/r.0.0.mca", [0]);
            Require(Run(work, "scan", world, "--no-cache", "--summary").Exit != 0, "a nonempty truncated region is still rejected");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void Java125TranslatesOnlyVanillaSigns()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            const string path = "region/r.0.0.mca";
            static NbtCompound Data(params NbtEntry[] fields) => new(fields);
            static NbtEntry Text(string key, string value) => new(key, new NbtString(value));
            var foreign = Data(Text("id", "Control"), Text("Command", "/say Foreign command"));
            var sheep = Data(Text("id", "Sheep"), Text("CustomName", "Foreign entity name"));
            NbtDocument Chunk(string text, params NbtCompound[] extraTiles)
            {
                var sign = Data(Text("id", "Sign"), Text("Text1", text), Text("CustomName", "Foreign sign name"));
                var level = Data(new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)),
                    new("TileEntities", new NbtList(NbtType.Compound, [sign, foreign, .. extraTiles])),
                    new("Entities", new NbtList(NbtType.Compound, [sheep])));
                return new("", Data(new NbtEntry("Level", level)));
            }
            var original = WorldDiscoveryTests.Region(Chunk("Welcome"), [0]);
            WorldDiscoveryTests.Put(world, path, original);
            var marker = CompressedNbt.WriteDocument(WorldMetadataTests.Classic(), NbtCompression.GZip);
            WorldDiscoveryTests.Put(world, "level.dat", marker);
            using var started = Success(Call(work, world, id, "start", "--minecraft-version", "1.2.5", "--source-language", "en_us", "--target-language", "zh_tw"));
            var slots = new TranslationSessionService(Store(work)).Read(id).Slots;
            Require(slots.Length == 1 && slots[0].Slot is { SourceText: "Welcome", Role: "sign.front.line" }, "Java 1.2.5 offers only vanilla sign text");
            foreach (var invalid in new[] { new string('譯', 16), "含\n換行" })
            {
                var revision = Submit(work, world, id, translate: _ => invalid);
                var response = Call(work, world, id, "plan", "--direct", "--revision", revision.ToString());
                Require(response.Exit == 3 && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(original), "invalid sign text is refused before writing");
            }
            var accepted = Submit(work, world, id, translate: _ => new string('譯', 15));
            var (plan, file) = Plan(work, world, id, accepted);
            Require(plan.ProfileVersion == "java-1.2.5", "the original sign profile is bound to the plan");
            using var applied = Success(Apply(work, world, file));
            using var completion = Success(Seal(work, world, id, accepted, Guid.NewGuid(), plan.PlanHash));
            var stored = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, path))).ReadChunk(0).Document;
            Require(stored.Root.Equals(Chunk(new string('譯', 15)).Root), "only vanilla sign data changes");
            var rollback = Run(work, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", State(work));
            Require(rollback.Exit == 8 && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(original), "rollback restores original Anvil bytes");
            var modernText = new NbtEntry("front_text", Data(Text("messages", "Later text")));
            foreach (var identity in new[] { "minecraft:sign", "minecraft:hanging_sign", "Sign" })
            {
                var mixed = WorldDiscoveryTests.Region(Chunk("Welcome", Data(Text("id", identity), Text("Text1", "Partial"), modernText)), [0]);
                WorldDiscoveryTests.Put(world, path, mixed);
                var scan = Run(work, "scan", world, "--minecraft-version", "1.2.5", "--path", path, "--no-cache");
                var denied = Call(work, world, Guid.NewGuid(), "start", "--minecraft-version", "1.2.5", "--source-language", "en_us", "--target-language", "zh_tw");
                Require(scan.Exit == 4 && scan.Output.Contains("UnsupportedClassicSignSchema", StringComparison.Ordinal) && denied.Exit == 4
                    && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(mixed), "mixed sign schemas cannot start a partial translation session");
            }
            WorldDiscoveryTests.Put(world, "moddata.dat", marker);
            var unknown = Run(work, "scan", world, "--minecraft-version", "1.2.5", "--path", "moddata.dat", "--no-cache");
            Require(unknown.Exit == 4 && unknown.Output.Contains("UnknownSourceLayout", StringComparison.Ordinal), "unknown root sidecars remain unsupported");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ClassicPlainTextRejectsValuesVanillaCannotLoad(string version)
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            const string path = "region/r.0.0.mca";
            const string force = "DIM-1/forcedchunks.dat";
            const string opaque = "/title @a title {\"text\":\"Later syntax\"}";
            static NbtCompound Data(params NbtEntry[] fields) => new(fields);
            var sign = Data(new("id", new NbtString("Sign")), new("Text1", new NbtString("Welcome")));
            var say = Data(new("id", new NbtString("Control")), new("Command", new NbtString("/say Hello miner")));
            var title = Data(new("id", new NbtString("Control")), new("Command", new NbtString(opaque)));
            var book = Data(new("id", new NbtShort(387)), new("tag", Data(new("title", new NbtString("Journal")),
                new("author", new NbtString("Miner")), new("pages", new NbtList(NbtType.String, [new NbtString("\"Plain\" page")])))));
            var chest = Data(new("id", new NbtString("Chest")), new("Items", new NbtList(NbtType.Compound, [book])));
            var chunk = new NbtDocument("", Data(new NbtEntry("Level", Data(new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)),
                new("TileEntities", new NbtList(NbtType.Compound, [sign, say, title, chest]))))));
            var original = WorldDiscoveryTests.Region(chunk, [0]);
            WorldDiscoveryTests.Put(world, path, original);
            void PutDat(string relative, NbtDocument doc) => WorldDiscoveryTests.Put(world, relative, CompressedNbt.WriteDocument(doc, NbtCompression.GZip));
            PutDat("level.dat", WorldMetadataTests.Classic());
            var tickets = new NbtDocument("", Data(new NbtEntry("TicketList", new NbtList(NbtType.Byte, []))));
            PutDat(force, tickets);
            using var started = Success(Call(work, world, id, "start", "--minecraft-version", version, "--source-language", "en_us", "--target-language", "zh_tw"));
            var state = new TranslationSessionService(Store(work)).Read(id);
            Require(state.Slots.Select(slot => slot.Slot.SourceText).Order().SequenceEqual(["\"Plain\" page", "Hello miner", "Journal", "Miner", "Welcome"]),
                "Classic profiles expose plain sign and book text, ignore empty chunk tickets, and leave later commands opaque");
            foreach (var (role, max) in new[] { ("sign.front.line", 15), ("book.title", 16), ("book.page", 256) })
            {
                var revision = Submit(work, world, id, translate: item => item.Context == role ? new string('x', max + 1) : item.SourceText);
                var response = Call(work, world, id, "plan", "--direct", "--revision", revision.ToString());
                Require(response.Exit == 3 && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(original),
                    "vanilla text length rejection occurs before world writes");
            }
            var accepted = Submit(work, world, id);
            var (plan, file) = Plan(work, world, id, accepted);
            Require(plan.ProfileVersion == "java-" + version, "the selected version binds the durable plan");
            using var applied = Success(Apply(work, world, file));
            using var completion = Success(Seal(work, world, id, accepted, Guid.NewGuid(), plan.PlanHash));
            var stored = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, path))).ReadChunk(0).Document;
            NbtValue? Read(params PathSegment[] fields) => NbtLocator.Resolve(stored.Root,
                new([PathSegment.ForKey("Level"), PathSegment.ForKey("TileEntities"), .. fields])).Value;
            Require(Read(PathSegment.ForIndex(0), PathSegment.ForKey("Text1")) is NbtString { Value: "譯文:Welcome" }
                && Read(PathSegment.ForIndex(1), PathSegment.ForKey("Command")) is NbtString { Value: "/say 譯文:Hello miner" }
                && Read(PathSegment.ForIndex(2), PathSegment.ForKey("Command")) is NbtString { Value: opaque }
                && Read(PathSegment.ForIndex(3), PathSegment.ForKey("Items"), PathSegment.ForIndex(0),
                    PathSegment.ForKey("tag"), PathSegment.ForKey("pages"), PathSegment.ForIndex(0)) is NbtString { Value: "譯文:\"Plain\" page" },
                "Plain sign and book text remains literal, and later commands stay untouched");
            var rollback = Run(work, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", State(work));
            Require(rollback.Exit == 8 && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(original), "rollback restores original region and padding");
            PutDat(force, new("", Data(new NbtEntry("TicketList", new NbtList(NbtType.Compound, [Data(new NbtEntry("ModId", new NbtString("mod")))])))));
            var unknown = Run(work, "scan", world, "--minecraft-version", "1.6.4", "--path", force, "--no-cache");
            Require(unknown.Exit == 4 && unknown.Output.Contains("UnsupportedClassicChunkTickets", StringComparison.Ordinal),
                "nonempty mod ticket state remains outside supported extraction coverage");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ClassicPlainTextScoreboardUsesVanillaFieldsAndLengths(string version)
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            const string regionPath = "region/r.0.0.mca", boardPath = "data/scoreboard.dat";
            const string addObjective = "/scoreboard objectives add score dummy ", addTeam = "scoreboard teams add miners ";
            const string execute = "execute @a ~ ~ ~ say Unsupported", option = "scoreboard teams option miners prefix Unsupported";
            static NbtCompound Data(params NbtEntry[] fields) => new(fields);
            static NbtEntry Text(string key, string value) => new(key, new NbtString(value));
            static NbtEntry List(string key, params NbtCompound[] values) => new(key, new NbtList(NbtType.Compound, values));
            static NbtCompound Command(string text) => Data(Text("id", "Control"), Text("Command", text));
            var level = Data(new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)), List("TileEntities",
                Command(addObjective + "New objective"), Command(addTeam + "New team"), Command(execute), Command(option)));
            var region = WorldDiscoveryTests.Region(new NbtDocument("", Data(new NbtEntry("Level", level))), [0]);
            WorldDiscoveryTests.Put(world, regionPath, region);
            WorldDiscoveryTests.Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Classic(), NbtCompression.GZip));
            var board = Data(List("Objectives", Data(Text("Name", "score"), Text("DisplayName", "Saved objective"))),
                List("Teams", Data(Text("Name", "miners"), Text("DisplayName", "Saved team"), Text("Prefix", "Before"), Text("Suffix", "After"))));
            var originalBoard = CompressedNbt.WriteDocument(new("", Data(new NbtEntry("data", board))), NbtCompression.GZip);
            WorldDiscoveryTests.Put(world, boardPath, originalBoard);

            using var started = Success(Call(work, world, id, "start", "--minecraft-version", version, "--source-language", "en_us", "--target-language", "zh_tw"));
            var state = new TranslationSessionService(Store(work)).Read(id);
            var fields = new Dictionary<string, int> { ["New objective"] = 32, ["New team"] = 32, ["Saved objective"] = 32, ["Saved team"] = 32, ["Before"] = 16, ["After"] = 16 };
            Require(state.Slots.Select(row => row.Slot.SourceText).Order().SequenceEqual(fields.Keys.Order()),
                "Classic profiles enumerate vanilla scoreboard fields and leave unregistered command forms opaque");
            foreach (var (source, limit) in fields)
            {
                var revision = Submit(work, world, id, translate: item => item.SourceText == source ? new string('譯', limit + 1) : item.SourceText);
                var preview = Call(work, world, id, "plan", "--direct", "--revision", revision.ToString());
                Require(preview.Exit == 3 && File.ReadAllBytes(Path.Combine(world, regionPath)).SequenceEqual(region)
                    && File.ReadAllBytes(Path.Combine(world, boardPath)).SequenceEqual(originalBoard), $"Reject oversized scoreboard field {source} before writing");
            }
            var accepted = Submit(work, world, id, translate: item => new string('譯', fields[item.SourceText]));
            var (plan, file) = Plan(work, world, id, accepted);
            using var applied = Success(Apply(work, world, file));
            var stored = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, regionPath))).ReadChunk(0).Document;
            var savedBoard = CompressedNbt.ReadDocument(File.ReadAllBytes(Path.Combine(world, boardPath)), NbtCompression.GZip).Document.Root;
            string? CommandAt(int index) => (NbtLocator.Resolve(stored.Root, new([PathSegment.ForKey("Level"), PathSegment.ForKey("TileEntities"), PathSegment.ForIndex(index), PathSegment.ForKey("Command")])).Value as NbtString)?.Value;
            string? BoardField(string collection, string key) => (NbtLocator.Resolve(savedBoard, new([PathSegment.ForKey("data"), PathSegment.ForKey(collection), PathSegment.ForIndex(0), PathSegment.ForKey(key)])).Value as NbtString)?.Value;
            Require(CommandAt(0) == addObjective + new string('譯', 32) && CommandAt(1) == addTeam + new string('譯', 32)
                && CommandAt(2) == execute && CommandAt(3) == option, "scoreboard commands retain identifiers and unregistered syntax stays opaque");
            Require(BoardField("Objectives", "DisplayName") == new string('譯', 32) && BoardField("Teams", "DisplayName") == new string('譯', 32)
                && BoardField("Teams", "Prefix") == new string('譯', 16) && BoardField("Teams", "Suffix") == new string('譯', 16)
                && BoardField("Objectives", "Name") == "score" && BoardField("Teams", "Name") == "miners", "saved scoreboard values use vanilla field names and keep identifiers");
            var rollback = Run(work, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", State(work));
            Require(rollback.Exit == 8 && File.ReadAllBytes(Path.Combine(world, regionPath)).SequenceEqual(region)
                && File.ReadAllBytes(Path.Combine(world, boardPath)).SequenceEqual(originalBoard), "scoreboard command and saved-data edits recover byte for byte");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void VersionlessAnvilUsesAnExplicitVersionForSessionApplyAndRollback()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            const string path = "region/r.0.0.mca";
            static NbtCompound Data(params NbtEntry[] fields) => new(fields);
            var sign = Data(new("id", new NbtString("Sign")), new("Text1", new NbtString("{\"text\":\"Welcome\",\"color\":\"gold\"}")),
                new("Text2", new NbtString("Old plain text")), new("Text3", new NbtString("null")));
            var command = Data(new("id", new NbtString("Control")), new("Command", new NbtString("/say Hello adventurer")));
            var title = Data(new("id", new NbtString("Control")), new("Command", new NbtString(
                "/execute @a ~ ~ ~ title @p subtitle {text:\"Arena\",color:gold,extra:[{score:{name:\"Points\",objective:\"Round\"}}]}")));
            const string givePrefix = "/give @p minecraft:diamond_sword 1 2 ";
            var give = Data(new("id", new NbtString("Control")), new("Command", new NbtString(givePrefix + "{display:{Name:\"Arena Token\"},ench:[{id:16s,lvl:2s}]}")));
            const string summon = "summon Item ~ ~1 ~ {Item:{id:minecraft:bow,Damage:,Count:1b,tag:{ench:[id:48,lvl:3],display:{Name:\"Runner's Bow\"}}}}";
            var drop = Data(new("id", new NbtString("Control")), new("Command", new NbtString(summon)));
            const string opaque = "summon Slime ~ ~ ~ {Size:2)";
            var control = Data(new("id", new NbtString("Control")), new("Command", new NbtString(opaque)));
            var chunk = new NbtDocument("", Data(new NbtEntry("Level", Data(new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)),
                new("TileEntities", new NbtList(NbtType.Compound, [sign, command, title, give, drop, control]))))));
            var original = WorldDiscoveryTests.Region(chunk, [0]);
            WorldDiscoveryTests.Put(world, path, original);
            WorldDiscoveryTests.Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Classic(), NbtCompression.GZip));
            string[] locales = ["--source-language", "en_us", "--target-language", "zh_tw"];
            Require(Call(work, world, Guid.NewGuid(), "start", locales).Exit == 4, "a versionless world cannot choose a writable grammar automatically");
            using var started = Success(Call(work, world, id, "start", [.. locales, "--minecraft-version", "1.8.1"]));
            var service = new TranslationSessionService(Store(work));
            Require(service.Read(id).Slots.Select(item => item.Slot.SourceText).Order().SequenceEqual(["Arena", "Arena Token", "Hello adventurer", "Old plain text", "Runner's Bow", "Welcome"]),
                "only encoded and plain display strings are translatable, preserving blank sign lines and score identifiers");
            var revision = Submit(work, world, id);
            var (plan, file) = Plan(work, world, id, revision);
            Require(plan.ProfileVersion == "java-1.8.1", "the plan binds the explicitly supplied engine profile");
            using var applied = Success(Apply(work, world, file));
            using var sealedSession = Success(Seal(work, world, id, revision, Guid.NewGuid(), plan.PlanHash));
            var stored = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, path))).ReadChunk(0).Document;
            var tiles = (NbtList)NbtLocator.Resolve(stored.Root, new([PathSegment.ForKey("Level"), PathSegment.ForKey("TileEntities")])).Value!;
            string Read(int tile, string key) => ((NbtString)((NbtCompound)tiles.Items[tile]).Entries.Single(entry => entry.Name == key).Value).Value;
            using var component = JsonDocument.Parse(Read(0, "Text1"));
            Require(component.RootElement.GetProperty("text").GetString() == "譯文:Welcome"
                && component.RootElement.GetProperty("color").GetString() == "gold"
                && JsonSerializer.Deserialize<string>(Read(0, "Text2")) == "譯文:Old plain text" && Read(0, "Text3") == "null",
                "translation preserves encoded sign presentation and never edits null lines");
            Require(Read(1, "Command") == "/say 譯文:Hello adventurer", "translation preserves the command verb");
            var outputTitle = Read(2, "Command"); var start = outputTitle.IndexOf('{');
            using var rendered = JsonDocument.Parse(outputTitle[start..]);
            Require(outputTitle[..start] == "/execute @a ~ ~ ~ title @p subtitle "
                && rendered.RootElement.GetProperty("text").GetString() == "譯文:Arena"
                && rendered.RootElement.GetProperty("extra")[0].GetProperty("score").GetProperty("objective").GetString() == "Round",
                "rewritten title preserves the command prefix and score identifier");
            Require(Read(3, "Command") == givePrefix + "{display:{Name:\"譯文:Arena Token\"},ench:[{id:16s,lvl:2s}]}",
                "a classic give rewrites only the exact item name string inside the data tag");
            Require(Read(4, "Command") == summon.Replace("Runner's Bow", "譯文:Runner's Bow", StringComparison.Ordinal),
                "an exact classic summon patch preserves the bare namespaced ID, blank data value and labeled list");
            Require(Read(5, "Command") == opaque, "a pre-existing opaque non-text command remains byte-for-byte unchanged");
            var rollback = Run(work, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", State(work));
            using var rolledBack = JsonDocument.Parse(rollback.Output);
            Require(rollback.Exit == 8 && rolledBack.RootElement.GetProperty("outcome").GetString() == "RolledBack"
                && File.ReadAllBytes(Path.Combine(world, path)).SequenceEqual(original), "rollback restores the complete region including the original padded bytes");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void PureMixedAndUnchangedSessionsUseThePublicPlanApplySealWorkflow()
    {
        foreach (var mode in new[] { "pure", "mixed", "unchanged" })
        {
            var world = mode == "pure" ? WorldQueryTests.CreateWorld() : WorldPreparationTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
            try
            {
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                Start(work, world, id); var revision = Submit(work, world, id, mode == "unchanged"); var store = Store(work); var accepted = store.Read(id)!;
                var (plan, file) = Plan(work, world, id, revision);
                Require(original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && store.Read(id)!.SequenceEqual(accepted), "preview preserves every world byte and the accepted session");
                using var applied = Success(Apply(work, world, file)); var receipt = Engine(world, work, plan.WorldId).ReadStatus(plan.PlanHash)!.Receipt!;
                Require(applied.RootElement.GetProperty("data").GetProperty("receipt").GetRawText() == ApplyReceiptJson.Write(receipt), "public receipt matches the durable prepared journal's verified receipt");
                Require(receipt.Success && receipt.Requested == new TranslationSessionService(store).Read(id).Slots.Length, "one original engine receipt accounts for every selected slot");
                if (mode == "unchanged") Require(receipt.Results.All(result => result.Outcome == OperationOutcome.Unchanged)
                    && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "explicit unchanged replies create no changed world images");
                else
                {
                    var readback = WorldQueryTests.Query(world, Path.Combine(work, "readback"));
                    Require(readback.Manifest.CoverageComplete && readback.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).All(item => item.Text.StartsWith("譯文:", StringComparison.Ordinal)), "new format-aware readback finds every selected translation");
                }
                var operation = Guid.NewGuid(); using var sealedResult = Success(Seal(work, world, id, revision, operation, plan.PlanHash));
                var status = sealedResult.RootElement.GetProperty("data").GetProperty("status");
                Require(status.GetProperty("applied").GetInt32() == receipt.Requested && status.GetProperty("revision").GetInt64() == revision + 1, "only complete durable world proof advances session completion");
                var sealedBytes = store.Read(id)!; using var replay = Success(Seal(work, world, id, revision, operation, plan.PlanHash));
                Require(store.Read(id)!.SequenceEqual(sealedBytes), "exact seal retry retains the original operation receipt without another revision");
                using var history = Success(Call(work, world, id, "status")); var historyData = history.RootElement.GetProperty("data");
                Require(historyData.GetProperty("status").GetProperty("applied").GetInt32() == receipt.Requested
                    && historyData.GetProperty("sourceFresh").ValueKind == JsonValueKind.Null && historyData.GetProperty("freshness").GetString() == "not-checked", "sealed status reports durable history without claiming current world freshness");
                Require(!sealedResult.RootElement.GetProperty("data").GetProperty("completion").TryGetProperty("inputBase64", out _), "public completion does not expose internal plan/AST input");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void PendingStaleRevisionAndChangedSourcesCannotProduceWritablePlans()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id);
            Require(Call(work, world, id, "plan", "--direct", "--revision", "0").Exit == 3, "pending replies cannot produce an apparently writable session plan");
            var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            Require(Call(work, world, id, "plan", "--direct", "--revision", "0").Exit == 3, "caller revision is an exact precondition");
            File.AppendAllText(Path.Combine(world, WorldQueryTests.Function(0)), "# external change\n");
            var output = Path.Combine(work, "stale-plan.json"); var changed = Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--output", output);
            Require(changed.Exit == 3 && !File.Exists(output) && Store(work).Read(id)!.SequenceEqual(before), "changed sources refuse publication and retain all validated replies");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MissingAndSubsetWorldProofsKeepTheSessionUnsealed()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            var missing = Seal(work, world, id, revision, Guid.NewGuid(), Hash256.Compute([1])); using var error = JsonDocument.Parse(missing.Output);
            Require(missing.Exit == 5 && error.RootElement.GetProperty("data").GetProperty("repair").GetString() == "inspect-apply-status-and-retry-seal", "missing journal returns a concrete repair without discarding session work");
            var (plan, _) = Plan(work, world, id, revision); var service = new TranslationSessionService(Store(work)); var selected = service.Read(id).Slots[0];
            var inventory = new WorldInventory(world, plan.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
            var subset = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("subset", new(selected.Slot, selected.Reply!))]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(world, plan.WorldId)).Prepare(snapshot, subset, true);
            var engine = Engine(world, work, plan.WorldId); Require(engine.Apply(prepared, true).Success, "control: a legitimate subset world apply has a successful durable receipt");
            Require(Seal(work, world, id, revision, Guid.NewGuid(), subset.PlanHash).Exit == 3 && Store(work).Read(id)!.SequenceEqual(before), "a successful subset receipt cannot seal omitted translations");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private static WorldApplyEngine Engine(string world, string work, Guid worldId, IArtifactStorage? storage = null) => new(new WorldInventory(world, worldId), storage ?? new ArtifactStorage(world, worldId),
        new FileSystemWorldBackupStore(Path.Combine(State(work), "objects")), new FileSystemApplyJournalStore(Path.Combine(State(work), "journals")));
    public static void ABlockedSessionSealPreservesCompletedWorldsAndOffersExactRetry()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); var revision = Submit(work, world, id); var (plan, file) = Plan(work, world, id, revision); using var applied = Success(Apply(work, world, file));
            var sessionFile = Path.Combine(State(work), "sessions", "session-" + id.ToString("N") + ".json"); var before = File.ReadAllBytes(sessionFile); var operation = Guid.NewGuid(); Invocation blocked;
            using (var locked = File.Open(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read)) blocked = Seal(work, world, id, revision, operation, plan.PlanHash);
            using var failure = JsonDocument.Parse(blocked.Output);
            Require(blocked.Exit == 5 && failure.RootElement.GetProperty("error").GetProperty("code").GetString() == "SESSION_SEAL_IO"
                && failure.RootElement.GetProperty("data").GetProperty("operationId").GetGuid() == operation
                && failure.RootElement.GetProperty("data").GetProperty("repair").GetString() == "retry-same-seal" && File.ReadAllBytes(sessionFile).SequenceEqual(before), "native blocked state publication returns exact retry identities and preserves accepted replies");
            using var sealedResult = Success(Seal(work, world, id, revision, operation, plan.PlanHash)); var sealedBytes = File.ReadAllBytes(sessionFile);
            using var replay = Success(Seal(work, world, id, revision, operation, plan.PlanHash)); Require(File.ReadAllBytes(sessionFile).SequenceEqual(sealedBytes), "unlocking permits exactly one completion");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void PartialApplyAndRollbackCannotSealUnfinishedSessionWork()
    {
        foreach (var rollback in new[] { false, true })
        {
            var world = WorldPreparationTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
            try
            {
                Start(work, world, id); var revision = Submit(work, world, id); var (plan, _) = Plan(work, world, id, revision); var before = Store(work).Read(id)!;
                var inventory = new WorldInventory(world, plan.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                    .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
                var storage = new ArtifactStorage(world, plan.WorldId, new WorldApplyTests.FailReplace(2, true));
                using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
                Require(!Engine(world, work, plan.WorldId, storage).Apply(prepared, true).Success, "controlled native second-replace failure leaves an unfinished journal");
                var operation = Guid.NewGuid(); Require(Seal(work, world, id, revision, operation, plan.PlanHash).Exit == 7 && Store(work).Read(id)!.SequenceEqual(before), "partial proof cannot seal or delete validated replies");
                var recovered = Run(work, ["recover", world, "--plan-hash", plan.PlanHash.Hex, "--world-offline", "--state-root", State(work), .. (rollback ? new[] { "--rollback" } : Array.Empty<string>())]);
                Require(recovered.Exit == (rollback ? 8 : 0), "public recovery preserves forward/rollback outcome distinctions");
                var completion = Seal(work, world, id, revision, operation, plan.PlanHash);
                Require(completion.Exit == (rollback ? 8 : 0), "only forward complete recovery can seal the same request");
                if (rollback)
                {
                    Require(Store(work).Read(id)!.SequenceEqual(before), "rollback keeps translated session work available");
                    var (freshPlan, freshFile) = Plan(work, world, id, revision); Require(freshPlan.PlanHash != plan.PlanHash, "new plan operation identity permits an explicit fresh attempt after terminal rollback");
                    using var reapplied = Success(Apply(work, world, freshFile)); using var resealed = Success(Seal(work, world, id, revision, Guid.NewGuid(), freshPlan.PlanHash));
                }
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void ArgumentsForeignWorldsAndExistingExportsNeverConsumeSessionWork()
    {
        var world = WorldQueryTests.CreateWorld(); var otherWorld = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Require(Call(work, world, id, "plan", "--direct").Exit == 2 && !Directory.Exists(State(work)), "missing revision rejects before repositories are created");
            Start(work, world, id); var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            Require(Call(work, world, id, "plan", "--revision", revision.ToString()).Exit == 2
                && Call(work, world, id, "plan", "--direct", "--revision", "18446744073709551615").Exit == 2, "mode and bounded revision are explicit");
            Require(Call(work, world, id, "seal", "--revision", revision.ToString(), "--plan-hash", Hash256.Compute([1]).Hex).Exit == 2, "seal never invents a missing retry operation identity");
            Require(Call(work, otherWorld, id, "plan", "--direct", "--revision", revision.ToString()).Exit == 3
                && Seal(work, otherWorld, id, revision, Guid.NewGuid(), Hash256.Compute([1])).Exit == 3, "byte-identical foreign worlds cannot consume session authority");
            var output = Path.Combine(work, "keep-plan.json"); File.WriteAllText(output, "user content");
            Require(Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--output", output).Exit == 3
                && File.ReadAllText(output) == "user content" && Store(work).Read(id)!.SequenceEqual(before), "existing exports and all accepted session bytes remain untouched");
            using var help = Success(Run(work, "translate", "plan", "--help"));
            Require(help.RootElement.GetProperty("data").GetProperty("options").EnumerateArray().Any(item => item.GetString() == "--direct"), "public machine help describes the new plan contract");
        }
        finally { Directory.Delete(world, true); Directory.Delete(otherWorld, true); Directory.Delete(work, true); }
    }
    public static void ExplicitUnchangedSlotsKeepTheirAcceptedNoWriteCompletionContract()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); using var exported = Success(Call(work, world, id, "export"));
            var data = exported.RootElement.GetProperty("data");
            var page = SessionExchangeJson.ReadPage(data.GetProperty("page").GetRawText());
            var reply = SessionExchangeJson.ReadReply(data.GetProperty("replyTemplate").GetRawText());
            reply = reply with { Slots = reply.Slots.Select((slot, index) => index == 0
                ? slot with { State = SessionReplyState.Translated, Text = "translated " + page.Slots.Single(source => source.SlotId == slot.SlotId).SourceText }
                : slot).ToImmutableArray() };
            var file = Path.Combine(work, "mixed-reply.json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply)); using var submitted = Success(Call(work, world, id, "submit", "--reply", file));
            var service = new TranslationSessionService(Store(work)); var state = service.Read(id); var selected = state.Slots.Single(slot => slot.State == SessionSlotState.Translated);
            var unchanged = state.Slots.Where(slot => slot.State == SessionSlotState.Unchanged).ToDictionary(slot => Path.Combine(world, slot.Slot.Address.Artifact.RelativePath), slot => File.ReadAllBytes(Path.Combine(world, slot.Slot.Address.Artifact.RelativePath)));
            var inventory = new WorldInventory(world, state.Source.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("explicit-translated-only", new(selected.Slot, selected.Reply!))]);
            var planFile = Path.Combine(work, "explicit-plan.json"); File.WriteAllText(planFile, PatchPlanJson.Write(plan));
            using var applied = Success(Apply(work, world, planFile)); using var completed = Success(Seal(work, world, id, state.Revision, Guid.NewGuid(), plan.PlanHash));
            var result = service.Read(id);
            Require(result.Slots.All(slot => slot.State == SessionSlotState.Applied) && result.Slots.Count(slot => slot.Reply is null) == unchanged.Count
                && unchanged.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "Unchanged means no requested translation: completion preserves null replies and does not invent world writes");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
