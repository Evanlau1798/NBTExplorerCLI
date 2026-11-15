using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class LootTextExtractionTests
{
    private static WorldSourceLocation Location(string file = "loot_table/test.json")
        => new(new(World, "datapacks/p/data/models/" + file, ArtifactKind.Json), WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
    private static PackMetadata Metadata => new(PackSourceKind.Datapack, 71, new(71, 0), new(71, int.MaxValue));
    public static void ExactOfficialRegistry(string fixtureRoot)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "discovery", "loot-function-profiles.json")));
        var versions = new[] { 3700, 3953, 4325, 4774 };
        foreach (var row in oracle.RootElement.GetProperty("profiles").EnumerateArray())
        {
            var profile = versions.Select(MinecraftProfiles.ForDataVersion).Single(profile => profile!.Id == row.GetProperty("profile").GetString())!;
            foreach (var id in row.GetProperty("identifiers").EnumerateArray()) Require(LootFunctionProfiles.IsKnown(profile, "minecraft:" + id.GetString()), "known identifiers agree with pinned official registry");
            Require(!LootFunctionProfiles.IsKnown(profile, "minecraft:future_text_function") && !LootFunctionProfiles.IsKnown(profile, "custom:set_name"), "unknown names never inherit a different registry namespace/profile");
        }
        Require(!LootFunctionProfiles.IsKnown(MinecraftProfiles.ForDataVersion(3953)!, "minecraft:discard")
            && !LootFunctionProfiles.IsKnown(MinecraftProfiles.ForDataVersion(4774)!, "minecraft:set_nbt"), "removed/future functions remain profile-specific");
    }
    public static void NestedLootNamesLoreAndBooks()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"pools":[{"entries":[{"type":"minecraft:item","name":"minecraft:written_book","functions":[
              {"function":"minecraft:set_name","name":{"text":"Name","color":"gold"}},
              {"function":"minecraft:set_lore","lore":[{"text":"Lore"}]},
              {"function":"minecraft:set_written_book_pages","pages":[[{"text":"Page"},{"text":""}]]},
              {"function":"minecraft:set_book_cover","title":"Title","author":"Author"},
              {"function":"minecraft:set_custom_model_data","value":2,"text":"Protected"}
            ]}]}],"conditions":[{"text":"Protected"}]}
            """);
        var result = PackTextExtractor.Extract(Location(), bytes, Metadata);
        var prefix = "/pools/0/entries/0/functions/";
        var expected = new[] { prefix + "0/name/text|item.name|Name", prefix + "1/lore/0/text|item.lore|Lore",
            prefix + "2/pages/0/0/text|book.page|Page", prefix + "2/pages/0/1/text|book.page|",
            prefix + "3/title|book.title|Title", prefix + "3/author|book.author|Author" };
        Require(result.Complete && result.Occurrences.Select(text => text.Address.NodePath.ToJsonPointer() + "|" + text.Address.SemanticRole + "|" + text.Text).Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Order(StringComparer.Ordinal)), "loot functions expose exact nested field paths; resource IDs, custom-model value and conditions stay protected");
    }
    public static void NestedModifiersComponentsAndUnknown()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            [{"function":"minecraft:filtered","item_filter":{},"modifier":{"function":"minecraft:sequence","functions":[
              {"function":"minecraft:set_components","components":{"minecraft:custom_name":{"text":"Component"},"minecraft:custom_data":{"value":"Protected"}}},
              {"function":"minecraft:set_lore","lore":["Scalar"]}
            ]}}]
            """);
        var result = PackTextExtractor.Extract(Location("item_modifier/test.json"), bytes, Metadata);
        Require(result.Complete && result.Occurrences.Select(text => text.Address.NodePath.ToJsonPointer() + "|" + text.Text).Order(StringComparer.Ordinal).SequenceEqual(new[] {
            "/0/modifier/functions/0/components/minecraft:custom_name/text|Component", "/0/modifier/functions/1/lore/0|Scalar" }.Order(StringComparer.Ordinal)), "item modifier arrays/filtered/sequence and explicit components retain exact source addresses");
        var unknown = PackTextExtractor.Extract(Location("item_modifier/test.json"), Encoding.UTF8.GetBytes("{\"function\":\"custom:future\",\"name\":{\"text\":\"Protected\"}}"), Metadata);
        Require(!unknown.Complete && unknown.Occurrences.IsEmpty && unknown.Diagnostics.Single().Code == "UnknownLootFunction", "unknown loot type is explicit incomplete, never generic name/value translation");
        var tag = PackTextExtractor.Extract(Location("tags/function/load.json"), Encoding.UTF8.GetBytes("{\"values\":[\"models:start\"]}"), Metadata);
        Require(tag.Complete && tag.Occurrences.IsEmpty && tag.TranslationKeys.IsEmpty, "function tag identifiers are known nontext data");
    }
    public static void BookCoverRejectsComponentTitle()
    {
        var result = PackTextExtractor.Extract(Location("item_modifier/cover.json"), Encoding.UTF8.GetBytes("{\"function\":\"minecraft:set_book_cover\",\"title\":{\"text\":\"Invalid\",\"color\":\"gold\"},\"author\":\"Valid author\"}"), Metadata);
        Require(!result.Complete && result.Diagnostics.Single().Code == "InvalidString" && result.Diagnostics.Single().Address.NodePath.ToJsonPointer() == "/title"
            && result.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/author", "official Filterable<String> cover title is not a text component; malformed user fixture stays incomplete");
    }
    public static void NestedItemDataCannotBeSilentlySkipped()
    {
        foreach (var field in new[] { "minecraft:container", "minecraft:bundle_contents", "minecraft:charged_projectiles", "minecraft:block_entity_data", "minecraft:entity_data" })
        {
            var json = JsonSerializer.Serialize(new { function = "minecraft:set_components", components = new Dictionary<string, object> { [field] = new { text = "Nested" } } });
            var result = PackTextExtractor.Extract(Location("item_modifier/nested.json"), Encoding.UTF8.GetBytes(json), Metadata);
            var code = field is "minecraft:block_entity_data" ? "MissingBlockEntityIdentity" : field is "minecraft:entity_data" ? "MissingEntityIdentity" : "InvalidLootList";
            Require(!result.Complete && result.Occurrences.IsEmpty && result.Diagnostics.Single().Code == code
                && result.Diagnostics.Single().Address.NodePath.ToJsonPointer() == "/components/" + field,
                "malformed nested item schemas remain exact incomplete sources, not empty successful extraction");
        }
    }
}
