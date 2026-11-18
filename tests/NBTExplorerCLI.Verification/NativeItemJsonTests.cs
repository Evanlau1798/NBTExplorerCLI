using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;

namespace NBTExplorerCLI.Verification;

internal static class NativeItemJsonTests
{
    public static void NestedItemsAndRawNbtFields()
    {
        foreach (var (format, modern) in new[] { (48, false), (71, true), (99, true) })
        {
            object Raw(string text) => modern ? new { text } : JsonSerializer.Serialize(new { text });
            object Item(string text, bool raw = false) => new { id = "minecraft:paper", components = new Dictionary<string, object> { [":custom_name"] = raw ? Raw(text) : new { text } } };
            var book = new { id = "minecraft:written_book", components = new Dictionary<string, object> {
                ["custom_name"] = new { text = "Book" }, ["written_book_content"] = new { title = "Title", author = "Author", pages = new[] { new { raw = new { text = "Page" }, filtered = "" } } } } };
            var entity = new Dictionary<string, object> { ["id"] = "armor_stand", ["CustomName"] = Raw("Stand") };
            if (modern) entity["equipment"] = new { mainhand = Item("Equipped", true) }; else entity["HandItems"] = new[] { Item("Equipped", true) };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, object> {
                ["minecraft:container"] = new[] { new { slot = 0, item = book } },
                ["bundle_contents"] = new[] { Item("Bundled") }, [":charged_projectiles"] = new[] { Item("Projectile") },
                ["block_entity_data"] = new { id = "sign", CustomName = Raw("Container"), front_text = new { messages = new[] { Raw("Sign") } }, Items = new[] { Item("Stored", true) } },
                ["entity_data"] = entity, ["minecraft:custom_data"] = new { value = "Protected", text = "Protected" } } });
            var source = new WorldSourceLocation(new(World, "datapacks/p/data/test/item_modifier/nested.json", ArtifactKind.Json), WorldSourceRole.PackJson,
                PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
            var result = PackTextExtractor.Extract(source, bytes, new(PackSourceKind.Datapack, format, new(format, 0), new(format, int.MaxValue)));
            var prefix = "/components/"; var bookPrefix = prefix + "minecraft:container/0/item/components/";
            var expected = new List<string> {
                bookPrefix + "custom_name/text|<physical>|item.name|Book", bookPrefix + "written_book_content/title|<physical>|book.title|Title",
                bookPrefix + "written_book_content/author|<physical>|book.author|Author", bookPrefix + "written_book_content/pages/0/raw/text|<physical>|book.page|Page",
                bookPrefix + "written_book_content/pages/0/filtered|<physical>|book.page|", prefix + "bundle_contents/0/components/:custom_name/text|<physical>|item.name|Bundled",
                prefix + ":charged_projectiles/0/components/:custom_name/text|<physical>|item.name|Projectile" };
            void RawRow(string path, string role, string text) => expected.Add(path + (modern ? "/text|<physical>|" : "|/text|") + role + "|" + text);
            RawRow(prefix + "block_entity_data/CustomName", "container.name", "Container");
            RawRow(prefix + "block_entity_data/front_text/messages/0", "sign.front.line", "Sign");
            RawRow(prefix + "block_entity_data/Items/0/components/:custom_name", "item.name", "Stored");
            RawRow(prefix + "entity_data/CustomName", "armor_stand.name", "Stand");
            RawRow(prefix + "entity_data/" + (modern ? "equipment/mainhand" : "HandItems/0") + "/components/:custom_name", "item.name", "Equipped");
            Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)),
                "native item/container/book and raw block/entity NBT fields retain exact physical versus embedded JSON paths for their own pack schema");
        }
    }
}
