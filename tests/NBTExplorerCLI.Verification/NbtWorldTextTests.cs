using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;

namespace NBTExplorerCLI.Verification;

internal static class NbtWorldTextTests
{
    private static WorldSourceLocation Region(bool entities = false) => new(new(World, entities ? "DIM-1/entities/r.-1.0.mca" : "DIM-1/region/r.-1.0.mca", entities ? ArtifactKind.Entities : ArtifactKind.Region),
        entities ? WorldSourceRole.Entities : WorldSourceRole.Terrain, "minecraft:the_nether", -1, 0);
    public static void SignsLecternsAndExactChunk()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var sign = C(E("id", S("minecraft:sign")), E("front_text", C(E("messages", L(Text("Front", modern))), E("color", S("red")))),
                E("back_text", C(E("messages", L(Text("Back", modern))), E("filtered_messages", L(Text("Filtered", modern))))));
            var hanging = C(E("id", S("minecraft:hanging_sign")), E("front_text", C(E("messages", L(Text("Hanging", modern))))));
            var content = C(E("pages", L(C(E("raw", Text("Lectern", modern))))));
            var book = C(E("id", S("minecraft:written_book")), E("components", C(E("minecraft:written_book_content", content))));
            var lectern = C(E("id", S("minecraft:lectern")), E("Book", book), E("Page", new NbtInt(0)));
            var root = C(E("DataVersion", new NbtInt(version)), E("block_entities", L(sign, hanging, lectern)), E("misc", C(E("text", S("Protected")))));
            var result = NbtTextExtractor.Extract(Region(), new("", root), new(-32, 0));
            var suffix = modern ? "/text|<physical>|" : "|/text|";
            var expected = new[] {
                "/block_entities/0/front_text/messages/0" + suffix + "sign.front.line|Front",
                "/block_entities/0/back_text/messages/0" + suffix + "sign.back.line|Back",
                "/block_entities/0/back_text/filtered_messages/0" + suffix + "sign.back.line|Filtered",
                "/block_entities/1/front_text/messages/0" + suffix + "hanging_sign.front.line|Hanging",
                "/block_entities/2/Book/components/minecraft:written_book_content/pages/0/raw" + suffix + "lectern.book.page|Lectern" };
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)), "sign sides/filtered lines, hanging sign and lectern book preserve exact physical or embedded component paths");
            Require(result.Occurrences.All(text => text.Address.Chunk == new ChunkPosition(-32, 0) && text.Address.Dimension == "minecraft:the_nether"
                && text.Address.Artifact == Region().Artifact), "all discovered world text preserves exact dimension/negative chunk and outer artifact");
        }
    }
    public static void EntitiesEquipmentAndPassengers()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var item = C(E("id", S("minecraft:stick")), E("components", C(E("minecraft:custom_name", Text("Equipment", modern)))));
            var emptySlot = C(E("Count", new NbtInt(1)));
            var passenger = C(E("id", S("minecraft:text_display")), E("text", Text("Display", modern)), E("transformation", C(E("value", S("Protected")))));
            var entity = C(E("id", S("minecraft:armor_stand")), E("CustomName", Text("Stand", modern)), E("Passengers", L(passenger)),
                modern ? E("equipment", C(E("mainhand", item))) : E("HandItems", L(item, emptySlot)), E("Tags", L(S("Protected"))));
            var root = C(E("DataVersion", new NbtInt(version)), E("Entities", L(entity)));
            var result = NbtTextExtractor.Extract(Region(entities: true), new("", root), new(-32, 0));
            var suffix = modern ? "/text|<physical>|" : "|/text|";
            var equipment = modern ? "/equipment/mainhand" : "/HandItems/0";
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/Entities/0/CustomName" + suffix + "armor_stand.name|Stand",
                "/Entities/0/Passengers/0/text" + suffix + "text_display.text|Display",
                "/Entities/0" + equipment + "/components/minecraft:custom_name" + suffix + "item.name|Equipment" }.Order(StringComparer.Ordinal)), "entity fields preserve their own profile and treat a count-only equipment slot as empty");

            var spawnData = C(E("entity", entity), E("custom_spawn_rules", C(E("text", S("Protected")))));
            var spawner = C(E("id", S("minecraft:mob_spawner")), E("SpawnData", spawnData),
                E("SpawnPotentials", L(C(E("data", spawnData), E("weight", new NbtInt(1))))));
            var template = C(E("DataVersion", new NbtInt(version)), E("blocks", L(C(E("nbt", spawner)))));
            var source = new WorldSourceLocation(new(World, $"generated/test/{(modern ? "structure" : "structures")}/example.nbt", ArtifactKind.Nbt), WorldSourceRole.Structure);
            var spawned = NbtTextExtractor.Extract(source, new("", template));
            var fields = new[] { "/CustomName" + suffix + "armor_stand.name|Stand", "/Passengers/0/text" + suffix + "text_display.text|Display",
                equipment + "/components/minecraft:custom_name" + suffix + "item.name|Equipment" };
            var expected = new[] { "/blocks/0/nbt/SpawnData/entity", "/blocks/0/nbt/SpawnPotentials/0/data/entity" };
            Require(spawned.Complete && spawned.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(
                expected.SelectMany(prefix => fields.Select(field => prefix + field)).Order(StringComparer.Ordinal)),
                "both mob spawner payloads expose native entity and item text at exact template paths, preserving spawn rules and weight");

            var blank = C(E("id", S("minecraft:mob_spawner")), E("SpawnData", C(E("entity", C()))));
            var unconfigured = C(E("DataVersion", new NbtInt(version)), E("blocks", L(C(E("nbt", blank)))));
            var empty = NbtTextExtractor.Extract(source, new("", unconfigured));
            Require(empty.Complete && empty.Occurrences.IsEmpty, "an unconfigured spawner's empty entity compound is a valid nontext source");
        }

        var unbound = C(E("Count", new NbtInt(1)), E("components", C(E("minecraft:custom_name", Text("Unbound", false)))));
        var equipped = C(E("id", S("minecraft:armor_stand")), E("HandItems", L(unbound)));
        var invalid = C(E("DataVersion", new NbtInt(3953)), E("Entities", L(equipped)));
        var resultWithUnboundText = NbtTextExtractor.Extract(Region(entities: true), new("", invalid), new(-32, 0));
        Require(!resultWithUnboundText.Complete && resultWithUnboundText.Diagnostics.Single().Code == "MissingItemIdentity",
            "an equipment item that carries text still requires an item identity");
    }
    public static void BossbarScoreboardAndCommandCandidates()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var scoreboard = C(E("DataVersion", new NbtInt(version)), E("data", C(
                E("Objectives", L(C(E("Name", S("Protected objective id")), E("DisplayName", Text("Objective", modern))))),
                E("Teams", L(C(E("Name", S("Protected team id")), E("DisplayName", Text("Team", modern)), E("MemberNamePrefix", Text("Prefix", modern)), E("MemberNameSuffix", Text("Suffix", modern))))))));
            var location = new WorldSourceLocation(new(World, modern ? "data/minecraft/scoreboard.dat" : "data/scoreboard.dat", ArtifactKind.Nbt), WorldSourceRole.SavedData);
            var result = NbtTextExtractor.Extract(location, new("", scoreboard));
            var suffix = modern ? "/text|<physical>|" : "|/text|";
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/data/Objectives/0/DisplayName" + suffix + "scoreboard.objective.name|Objective", "/data/Teams/0/DisplayName" + suffix + "scoreboard.team.name|Team",
                "/data/Teams/0/MemberNamePrefix" + suffix + "scoreboard.team.prefix|Prefix", "/data/Teams/0/MemberNameSuffix" + suffix + "scoreboard.team.suffix|Suffix" }.Order(StringComparer.Ordinal)), "scoreboard display fields are separate roles; identifier names protected");
            var boss = C(E("Name", Text("Boss", modern)), E("Players", L(S("Protected"))));
            var bossRoot = modern ? C(E("DataVersion", new NbtInt(version)), E("data", C(E("golden:boss", boss))))
                : C(E("Data", C(E("DataVersion", new NbtInt(version)), E("CustomBossEvents", C(E("golden:boss", boss))))));
            var bossSource = new WorldSourceLocation(new(World, modern ? "data/minecraft/custom_boss_events.dat" : "level.dat", ArtifactKind.Nbt), modern ? WorldSourceRole.SavedData : WorldSourceRole.Level);
            var bosses = NbtTextExtractor.Extract(bossSource, new("", bossRoot));
            var prefix = modern ? "/data/golden:boss/Name" : "/Data/CustomBossEvents/golden:boss/Name";
            Require(bosses.Complete && Row(bosses.Occurrences.Single()) == prefix + suffix + "bossbar.name|Boss", "inline and separately stored bossbars use exact declared field roots");
        }
        const string command = "execute as @a run tellraw @s {\"text\":\"Pending\"}";
        var commandRoot = C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(C(E("id", S("minecraft:command_block")), E("Command", S(command))))));
        var pending = NbtTextExtractor.Extract(Region(), new("", commandRoot), new(-32, 0));
        Require(!pending.Complete && pending.Occurrences.IsEmpty && pending.PendingCommands.Single().Command == command
            && pending.PendingCommands.Single().Address.NodePath.ToJsonPointer() == "/block_entities/0/Command", "command string is handed to grammar parser with exact source, never translated wholesale or silently complete");
    }
    public static void TypesReachActualFieldAdapter()
    {
        var sign = C(E("id", S("minecraft:sign")), E("front_text", C(E("messages", L(Text("Selected", false))))), E("back_text", C(E("messages", L(S("not JSON, excluded"))))));
        var command = C(E("id", S("minecraft:command_block")), E("Command", S("tellraw @s {\"text\":\"Excluded\"}")));
        var root = C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(sign, command)));
        var scope = new SnapshotScope([], [], ["sign.front.line"], []);
        var result = NbtTextExtractor.Extract(Region(), new("", root), new(-32, 0), scope: scope);
        Require(result.Complete && result.Occurrences.Single().Text == "Selected" && result.PendingCommands.IsEmpty, "types select actual field parsing; excluded invalid embedded component and pending command cannot pollute selected scope");
    }
}
