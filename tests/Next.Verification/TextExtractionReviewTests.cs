using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.NbtItemTextTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class TextExtractionReviewTests
{
    private static WorldSourceLocation Modifier => new(new(World, "datapacks/p/data/test/item_modifier/name.json", ArtifactKind.Json), WorldSourceRole.PackJson,
        PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
    private static PackMetadata Pack => new(PackSourceKind.Datapack, 71, new(71, 0), new(71, int.MaxValue));
    public static void EmptyNamespaceAliases()
    {
        foreach (var (format, version, modern) in new[] { (48, 3953, false), (71, 4325, true), (99, 4774, true) })
        {
            var pack = new PackMetadata(PackSourceKind.Datapack, format, new(format, 0), new(format, int.MaxValue));
            var result = PackTextExtractor.Extract(Modifier, Encoding.UTF8.GetBytes("{\"function\":\"set_components\",\"components\":{\":custom_name\":{\"text\":\"Name\"}}}"), pack);
            Require(result.Complete && result.Occurrences.Length == 1 && Row(result.Occurrences.Single()) == "/components/:custom_name/text|<physical>|item.name|Name", "empty default namespace retains the exact JSON key");
            var nestedBytes = Encoding.UTF8.GetBytes("{\"function\":\"set_components\",\"components\":{\":bundle_contents\":{}}}");
            Require(PackTextExtractor.Extract(Modifier, nestedBytes, pack).Diagnostics.Single().Code == "InvalidLootList", "empty namespace malformed nested data remains incomplete");
            Require(PackTextExtractor.Extract(Modifier, nestedBytes, pack, scope: new([], [], ["language.value"], [])).Complete, "empty namespace follows the same type scope");
            var player = new WorldSourceLocation(new(World, "playerdata/test.dat", ArtifactKind.Nbt), WorldSourceRole.Player);
            var named = C(E("id", S("minecraft:paper")), E("components", C(E(":custom_name", Text("Name", modern)))));
            var nbt = NbtTextExtractor.Extract(player, new("", C(E("DataVersion", new NbtInt(version)), E("Inventory", L(named)))));
            Require(nbt.Complete && nbt.Occurrences.Length == 1 && nbt.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/Inventory/0/components/:custom_name" + (modern ? "/text" : ""), "empty default namespace retains the exact NBT key and representation-specific leaf");
            var aliases = new[] { "custom_name", ":custom_name", "minecraft:custom_name" };
            for (var left = 0; left < aliases.Length; left++)
                for (var right = left + 1; right < aliases.Length; right++)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new { function = "set_components", components = new Dictionary<string, string> { [aliases[left]] = "A", [aliases[right]] = "B" } });
                    var conflict = PackTextExtractor.Extract(Modifier, Encoding.UTF8.GetBytes(json), pack);
                    Require(!conflict.Complete && conflict.Occurrences.IsEmpty && conflict.Diagnostics.Single().Code == "AmbiguousItemComponent", "all JSON spellings of a default namespace share an ambiguity group");
                    var item = C(E("id", S("minecraft:paper")), E("components", C(E(aliases[left], Text("A", modern)), E(aliases[right], Text("B", modern)))));
                    var nbtConflict = NbtTextExtractor.Extract(player, new("", C(E("DataVersion", new NbtInt(version)), E("Inventory", L(item)))));
                    Require(!nbtConflict.Complete && nbtConflict.Occurrences.IsEmpty && nbtConflict.Diagnostics.Single().Code == "AmbiguousItemComponent", "all NBT spellings of a default namespace share an ambiguity group");
                }
        }
    }
    public static void ComponentAliasesKeepLiteralKeys()
    {
        var result = PackTextExtractor.Extract(Modifier, Encoding.UTF8.GetBytes("""
            {"function":"set_components","components":{"custom_name":{"text":"Short"},"lore":["Lore"],"custom:custom_name":{"text":"Protected"}}}
            """), Pack);
        Require(result.Complete && result.Occurrences.Select(Row).SequenceEqual(new[] {
            "/components/custom_name/text|<physical>|item.name|Short", "/components/lore/0|<physical>|item.lore|Lore" }), "short default namespace keys retain literal JSON addresses");
        var nested = PackTextExtractor.Extract(Modifier, Encoding.UTF8.GetBytes("{\"function\":\"set_components\",\"components\":{\"bundle_contents\":{}}}"), Pack);
        Require(!nested.Complete && nested.Diagnostics.Single().Code == "InvalidLootList", "short malformed nested component key cannot bypass incomplete outcome");
        var duplicate = PackTextExtractor.Extract(Modifier, Encoding.UTF8.GetBytes("""
            {"function":"set_components","components":{"custom_name":"A","minecraft:custom_name":"B"}}
            """), Pack);
        Require(!duplicate.Complete && duplicate.Occurrences.IsEmpty && duplicate.Diagnostics.Single().Code == "AmbiguousItemComponent", "semantic aliases are ambiguous instead of silently selecting one");
        var player = new WorldSourceLocation(new(World, "playerdata/test.dat", ArtifactKind.Nbt), WorldSourceRole.Player);
        var item = C(E("id", S("minecraft:paper")), E("components", C(E("custom_name", Text("NBT short", false)))));
        var nbt = NbtTextExtractor.Extract(player, new("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item)))));
        Require(nbt.Complete && Row(nbt.Occurrences.Single()) == "/Inventory/0/components/custom_name|/text|item.name|NBT short", "NBT registry keys use the same identifier semantics and preserve literal paths");
        var ambiguous = C(E("id", S("minecraft:paper")), E("components", C(E("custom_name", Text("A", false)), E("minecraft:custom_name", Text("B", false)))));
        var conflict = NbtTextExtractor.Extract(player, new("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(ambiguous)))));
        Require(!conflict.Complete && conflict.Occurrences.IsEmpty && conflict.Diagnostics.Single().Code == "AmbiguousItemComponent", "NBT aliases also reject ambiguous component identity");
    }
    public static void LegacyNestedItemsHavePhysicalPaths()
    {
        var named = C(E("id", S("minecraft:paper")), E("tag", C(E("display", C(E("Name", Text("Nested", false)))))));
        var bundle = C(E("id", S("minecraft:bundle")), E("tag", C(E("Items", L(named)))));
        var crossbow = C(E("id", S("minecraft:crossbow")), E("tag", C(E("ChargedProjectiles", L(named)))));
        var protectedItem = C(E("id", S("minecraft:paper")), E("tag", C(E("Items", L(named)), E("ChargedProjectiles", L(named)))));
        var player = new WorldSourceLocation(new(World, "playerdata/test.dat", ArtifactKind.Nbt), WorldSourceRole.Player);
        var result = NbtTextExtractor.Extract(player, new("", C(E("DataVersion", new NbtInt(3700)), E("Inventory", L(bundle, crossbow, protectedItem)))));
        Require(result.Complete && result.Occurrences.Select(Row).SequenceEqual(new[] {
            "/Inventory/0/tag/Items/0/tag/display/Name|/text|item.name|Nested",
            "/Inventory/1/tag/ChargedProjectiles/0/tag/display/Name|/text|item.name|Nested" }), "legacy bundle/crossbow recurse known item fields; same custom tags on other items stay protected");
    }
    public static void VanillaDataRequiresExactLayoutPath()
    {
        foreach (var (version, path, dimension, supported) in new (int, string, string?, bool)[] {
            (3953, "data/scoreboard.dat", null, true), (4774, "data/minecraft/scoreboard.dat", null, true),
            (3953, "DIM-1/data/scoreboard.dat", "minecraft:the_nether", true),
            (4774, "dimensions/test/moon/data/minecraft/scoreboard.dat", "test:moon", true),
            (4774, "data/mod/scoreboard.dat", null, false), (3953, "data/mod/scoreboard.dat", null, false),
            (3953, "data/minecraft/scoreboard.dat", null, false), (4774, "data/scoreboard.dat", null, false),
            (4774, "DIM-1/data/minecraft/scoreboard.dat", "minecraft:the_nether", false) })
        {
            var source = new WorldSourceLocation(new(World, path, ArtifactKind.Nbt), WorldSourceRole.SavedData, dimension);
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", C(E("Objectives", L(C(E("DisplayName", Text("Name", version == 4774)))))))));
            var result = NbtTextExtractor.Extract(source, document);
            Require(supported ? result.Complete && result.Occurrences.Single().Text == "Name"
                : !result.Complete && result.Occurrences.IsEmpty, "vanilla saved-data authorization requires exact layout/dimension/namespace: " + path);
        }
    }
    public static void PackDependencyIsVerifiedAtExtraction()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            const string path = "datapacks/p/data/test/advancement/a.json";
            Put(root, path, Encoding.UTF8.GetBytes("{\"display\":{\"title\":{\"text\":\"Main\",\"hover_event\":{\"action\":\"show_text\",\"value\":\"Hover\"}}}}"));
            var scan = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [path])));
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":71,\"description\":\"New schema\"}}"));
            var row = scan.ReadSources().Single();
            Require(row.Outcome == SourceOutcome.Failed && row.DiagnosticCode == "PackMetadataChanged" && row.Text is null, "metadata outside exact artifact scope still binds member interpretation");
            var fresh = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [path]))).ReadSources().Single();
            Require(fresh.Text!.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Main", "Hover" }), "positive control: fresh metadata selects modern hover schema");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void FunctionRetentionUsesExtractionBudget()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            const string path = "datapacks/p/data/test/function/main.mcfunction";
            foreach (var length in new[] { 4096, 128, 2 })
            {
                Put(root, path, Encoding.UTF8.GetBytes("say " + new string('x', length)));
                var scan = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [path]), new(MaxDecodedBytes: 512)));
                var row = scan.ReadSources().Single();
                Require(length == 2 ? row.DiagnosticCode == "CommandGrammarPending" && row.Text!.PendingCommands.Length == 1
                    : row.Outcome == SourceOutcome.Failed && row.DiagnosticCode == "LimitExceeded" && row.Text is null,
                    "byte and UTF16/address retention limits reject before decoded allocation; small command remains a pending candidate");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    public static void NestedUnsupportedHonorsTypeScope()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"function\":\"set_components\",\"components\":{\"minecraft:bundle_contents\":{}}}");
        var excluded = PackTextExtractor.Extract(Modifier, bytes, Pack, scope: new([], [], ["language.value"], []));
        Require(excluded.Complete && excluded.Occurrences.IsEmpty, "unrelated language type excludes nested-item parsing");
        var selected = PackTextExtractor.Extract(Modifier, bytes, Pack, scope: new([], [], ["item.name"], []));
        Require(!selected.Complete && selected.Diagnostics.Single().Code == "InvalidLootList", "possibly matching malformed nested item remains incomplete");
    }
}
