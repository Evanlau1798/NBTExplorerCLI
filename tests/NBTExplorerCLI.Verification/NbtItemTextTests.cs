using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class NbtItemTextTests
{
    internal static NbtEntry E(string name, NbtValue value) => new(name, value);
    internal static NbtCompound C(params NbtEntry[] entries) => new(entries);
    internal static NbtString S(string value) => new(value);
    internal static NbtList L(params NbtValue[] values) => new(values.Length == 0 ? NbtType.Compound : values[0].Type, values);
    internal static NbtValue Text(string text, bool modern) => modern ? C(E("text", S(text))) : S(JsonSerializer.Serialize(new { text }));
    internal static string Row(TextOccurrence text) => string.Join('|', text.Address.NodePath.ToJsonPointer(), text.Address.ComponentPath?.ToJsonPointer() ?? "<physical>", text.Address.SemanticRole, text.Text);
    private static WorldSourceLocation Player => new(new(World, "playerdata/literal.dat", ArtifactKind.Nbt), WorldSourceRole.Player);
    public static void ComponentItemsAndNestedBooks()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var pages = L(C(E("raw", Text("Page", modern)), E("filtered", Text("Filtered page", modern))), C(E("raw", S(modern ? "" : "\"\""))));
            var content = C(E("title", C(E("raw", S("Title")), E("filtered", S("Filtered title")))), E("author", S("Scribe")), E("pages", pages));
            var bookComponents = C(E("minecraft:custom_name", Text("Book name", modern)), E("minecraft:lore", L(Text("Lore", modern))), E("minecraft:written_book_content", content));
            var book = C(E("id", S("minecraft:written_book")), E("count", new NbtInt(1)), E("components", bookComponents));
            var components = C(
                E("minecraft:custom_name", Text("Chest name", modern)), E("minecraft:container", L(C(E("slot", new NbtInt(0)), E("item", book)))),
                E("minecraft:bundle_contents", L(C(E("id", S("minecraft:paper")), E("components", C(E("minecraft:custom_name", Text("Bundled", modern))))))),
                E("minecraft:charged_projectiles", L(C(E("id", S("minecraft:arrow")), E("components", C(E("minecraft:custom_name", Text("Projectile", modern))))))),
                E("minecraft:custom_data", C(E("value", S("Protected")), E("CustomName", Text("Not a component field", modern)))));
            var item = C(E("id", S("minecraft:chest")), E("count", new NbtInt(1)), E("components", components));
            var root = C(E("DataVersion", new NbtInt(version)), E("Inventory", L(item)), E("unknown", C(E("text", S("Protected")))));
            var result = NbtTextExtractor.Extract(Player, new("", root));
            var expected = new List<string>();
            void Component(string path, string role, string text, bool scalar = false)
                => expected.Add(modern ? path + (scalar ? "" : "/text") + "|<physical>|" + role + "|" + text : path + "|" + (scalar ? "" : "/text") + "|" + role + "|" + text);
            Component("/Inventory/0/components/minecraft:custom_name", "item.name", "Chest name");
            var nested = "/Inventory/0/components/minecraft:container/0/item/components/";
            Component(nested + "minecraft:custom_name", "item.name", "Book name");
            Component(nested + "minecraft:lore/0", "item.lore", "Lore");
            Component(nested + "minecraft:written_book_content/pages/0/raw", "book.page", "Page");
            Component(nested + "minecraft:written_book_content/pages/0/filtered", "book.page", "Filtered page");
            Component(nested + "minecraft:written_book_content/pages/1/raw", "book.page", "", scalar: true);
            expected.Add(nested + "minecraft:written_book_content/title/raw|<physical>|book.title|Title");
            expected.Add(nested + "minecraft:written_book_content/title/filtered|<physical>|book.title|Filtered title");
            expected.Add(nested + "minecraft:written_book_content/author|<physical>|book.author|Scribe");
            Component("/Inventory/0/components/minecraft:bundle_contents/0/components/minecraft:custom_name", "item.name", "Bundled");
            Component("/Inventory/0/components/minecraft:charged_projectiles/0/components/minecraft:custom_name", "item.name", "Projectile");
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)), "component item/container/bundle/projectile/book slots equal exact path/role/text set; custom_data/value remains protected");
            Require(result.Occurrences.All(text => text.Type == NbtType.String && text.Address.Artifact == Player.Artifact), "all item fields retain physical source and String type");
        }
    }
    public static void ItemTagItemsAndArtifactVersion()
    {
        var item = C(E("id", S("minecraft:written_book")), E("Count", new NbtByte(1)), E("tag", C(
            E("display", C(E("Name", Text("Tagged name", false)), E("Lore", L(Text("Tagged lore", false))))),
            E("title", S("Title")), E("author", S("Author")), E("pages", L(Text("Page", false), S("\"\""))), E("value", S("Protected")))));
        var root = C(E("DataVersion", new NbtInt(3700)), E("Inventory", L(item)));
        var result = NbtTextExtractor.Extract(Player, new("", root));
        Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
            "/Inventory/0/tag/display/Name|/text|item.name|Tagged name", "/Inventory/0/tag/display/Lore/0|/text|item.lore|Tagged lore",
            "/Inventory/0/tag/title|<physical>|book.title|Title", "/Inventory/0/tag/author|<physical>|book.author|Author",
            "/Inventory/0/tag/pages/0|/text|book.page|Page", "/Inventory/0/tag/pages/1||book.page|" }.Order(StringComparer.Ordinal)), "own DV3700 chooses old tags, independently from containing world's layout/profile");
        var sameFamily = NbtTextExtractor.Extract(Player, new("", C(E("DataVersion", new NbtInt(3701)), E("Inventory", L(item)))));
        Require(sameFamily.Complete && sameFamily.Occurrences.Select(Row).SequenceEqual(result.Occurrences.Select(Row)),
            "an artifact within the same format range has the same tag slots");
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => NbtTextExtractor.Extract(Player, new("", C(E("DataVersion", new NbtInt(1975)), E("Inventory", L(item))))));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => NbtTextExtractor.Extract(Player, new("", C(E("Inventory", L(item))))));

        var eggData = C(E("CustomName", Text("Spawned creature", false)));
        TextExtractionResult TaggedEntity(string itemId, NbtCompound entity, int dataVersion = 3700)
        {
            var stack = C(E("id", S(itemId)), E("tag", C(E("EntityTag", entity))));
            return NbtTextExtractor.Extract(Player, new("", C(E("DataVersion", new NbtInt(dataVersion)), E("Inventory", L(stack)))));
        }
        foreach (var version in new[] { 3465, 3700 })
        {
            var defaultEgg = TaggedEntity("minecraft:fox_spawn_egg", eggData, version);
            Require(defaultEgg.Complete && Row(defaultEgg.Occurrences.Single()) == "/Inventory/0/tag/EntityTag/CustomName|/text|entity.name|Spawned creature",
                $"DataVersion {version}: a registered spawn egg may omit EntityTag.id and still carry live entity text");
        }
        foreach (var version in new[] { 3465, 3578, 3689 })
        {
            var earlyBreeze = TaggedEntity("minecraft:breeze_spawn_egg", eggData, version);
            Require(!earlyBreeze.Complete && earlyBreeze.Diagnostics.Single().Code == "MissingEntityIdentity",
                $"DataVersion {version} does not infer an entity from an egg added after that version");
        }
        var overrideEgg = TaggedEntity("minecraft:fox_spawn_egg", C(E("id", S("minecraft:armor_stand")), E("CustomName", Text("Override", false))));
        Require(overrideEgg.Complete && overrideEgg.Occurrences.Single().Address.SemanticRole == "armor_stand.name", "an explicit entity type wins over a spawn egg's default");
        foreach (var unknown in new[] { "minecraft:stone", "minecraft:made_up_spawn_egg", "mod:fox_spawn_egg" })
        {
            var unbound = TaggedEntity(unknown, eggData);
            Require(!unbound.Complete && unbound.Diagnostics.Single().Code == "MissingEntityIdentity",
                $"{unknown}: only registered vanilla spawn eggs can supply a default entity identity");
        }
        TextExtractionResult TaggedBlock(string itemId, NbtCompound block)
        {
            var stack = C(E("id", S(itemId)), E("tag", C(E("BlockEntityTag", block))));
            return NbtTextExtractor.Extract(Player, new("", C(E("DataVersion", new NbtInt(3465)), E("Inventory", L(stack)))));
        }
        var paper = C(E("id", S("minecraft:paper")), E("tag", C(E("display", C(E("Name", Text("Nested name", false)))))));
        var chest = TaggedBlock("minecraft:chest", C(E("Items", L(paper))));
        Require(chest.Complete && chest.Occurrences.Single().Text == "Nested name", "a chest supplies its block entity identity and exact nested item text");
        var banner = TaggedBlock("minecraft:white_banner", C(E("CustomName", Text("Banner name", false))));
        Require(banner.Complete && banner.Occurrences.Single().Text == "Banner name", "a vanilla colored banner supplies its banner identity");
        var pattern = L(C(E("Color", new NbtInt(1)), E("Pattern", S("mr"))));
        var shield = TaggedBlock("minecraft:shield", C(E("Base", new NbtInt(2)), E("Patterns", pattern)));
        Require(shield.Complete && shield.Occurrences.IsEmpty, "shield pattern identifiers are not visible text");
        Require(!TaggedBlock("mod:white_banner", C(E("CustomName", Text("Unknown", false)))).Complete
            && !TaggedBlock("minecraft:shield", C(E("Items", L(paper)))).Complete,
            "unknown item identities and unexpected shield payloads do not acquire a text schema");
        var ambiguous = TaggedBlock("minecraft:chest", C(E("id", S("minecraft:chest")), E("id", S("minecraft:banner")), E("Items", L(paper))));
        Require(!ambiguous.Complete && ambiguous.Occurrences.IsEmpty && ambiguous.Diagnostics.Any(row => row.Code == "AmbiguousOrInvalidField"),
            "a duplicated identity cannot grant fallback access to nested text");
    }
    public static void EmbeddedBlockEntityAndEntityItems()
    {
        foreach (var usesTags in new[] { true, false })
        {
            var itemData = usesTags ? C(E("display", C(E("Name", Text("Nested item", false))))) : C(E("minecraft:custom_name", Text("Nested item", false)));
            var field = usesTags ? "tag" : "components";
            var nested = C(E("id", S("minecraft:paper")), E(field, itemData));
            var block = C(E("id", S("minecraft:chest")), E("CustomName", Text("Container", false)), E("Items", L(nested)));
            var entity = C(E("id", S("minecraft:armor_stand")), E("CustomName", Text("Entity", false)), E("HandItems", L(nested)));
            var blockKey = usesTags ? "BlockEntityTag" : "minecraft:block_entity_data";
            var entityKey = usesTags ? "EntityTag" : "minecraft:entity_data";
            var root = C(E("DataVersion", new NbtInt(usesTags ? 3700 : 3953)), E("Inventory", L(
                C(E("id", S("minecraft:chest")), E(field, C(E(blockKey, block)))), C(E("id", S("minecraft:armor_stand")), E(field, C(E(entityKey, entity)))))));
            var result = NbtTextExtractor.Extract(Player, new("", root));
            var nestedName = usesTags ? "/tag/display/Name" : "/components/minecraft:custom_name";
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/Inventory/0/" + field + "/" + blockKey + "/CustomName|/text|container.name|Container",
                "/Inventory/0/" + field + "/" + blockKey + "/Items/0" + nestedName + "|/text|item.name|Nested item",
                "/Inventory/1/" + field + "/" + entityKey + "/CustomName|/text|armor_stand.name|Entity",
                "/Inventory/1/" + field + "/" + entityKey + "/HandItems/0" + nestedName + "|/text|item.name|Nested item" }.Order(StringComparer.Ordinal)), "block/entity data nested in items retains names and nested inventory items for old tags and components");
        }
    }
}
