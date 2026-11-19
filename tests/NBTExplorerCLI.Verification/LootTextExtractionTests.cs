using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

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
    public static void Java12111CraftingAndEmptyEffectEnchantmentText()
    {
        var pack = new PackMetadata(PackSourceKind.Datapack, null, new(94, 1), new(94, 1));
        TextExtractionResult Read(string file, string json) => PackTextExtractor.Extract(Location(file), Encoding.UTF8.GetBytes(json), pack);
        var recipe = Read("recipe/example.json", """{"type":"minecraft:crafting_shaped","group":"Protected","pattern":["X"],"key":{"X":"minecraft:paper"},"result":{"id":"minecraft:paper","components":{"minecraft:custom_name":{"text":"Prize"}}}}""");
        Require(recipe.Complete && recipe.Occurrences.Single().Text == "Prize" && recipe.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/result/components/minecraft:custom_name/text",
            "crafting recognizes the declared result item and preserves the machine group name");
        var air = Read("recipe/invalid.json", """{"type":"minecraft:crafting_shapeless","ingredients":["minecraft:copper_block"],"result":{"id":"minecraft:air"}}""");
        Require(!air.Complete, "an air result is invalid and remains unresolved");
        var enchantment = Read("enchantment/example.json", """{"description":{"translate":"Tax Evasion","fallback":"Tax Evasion"},"effects":{}}""");
        Require(enchantment.Complete && enchantment.Occurrences.Single().Text == "Tax Evasion"
            && enchantment.TranslationKeys.Single().Key == "Tax Evasion", "extract the description fallback while preserving the exact translation key");
        Require(!Read("enchantment/unknown.json", """{"description":"Name","effects":{"unknown":{"text":"Nested"}}}""").Complete,
            "an unproved enchantment effect cannot be claimed as fully extracted");
    }

    public static void CraftingResultProfiles()
    {
        foreach (var (major, minor, idField, directory) in new[]
        {
            (26, 0, "item", "recipes"), (48, 0, "id", "recipe"), (71, 0, "id", "recipe"),
            (94, 1, "id", "recipe"), (99, 0, "id", "recipe")
        })
        {
            var metadata = new PackMetadata(PackSourceKind.Datapack, major < 65 ? major : null, new(major, minor), new(major, minor));
            foreach (var shape in new[] { "shaped", "shapeless" })
            {
                var input = major <= 48 ? """{"item":"minecraft:stone"}""" : "\"minecraft:stone\"";
                var ingredients = shape == "shaped" ? "\"pattern\":[\"X\"],\"key\":{\"X\":" + input + "}" : "\"ingredients\":[" + input + "]";
                TextExtractionResult Read(string result, string? type = null, string? folder = null) => PackTextExtractor.Extract(
                    Location($"{folder ?? directory}/nested/test.json"),
                    Encoding.UTF8.GetBytes($$"""{"type":"{{type ?? "crafting_" + shape}}",{{ingredients}},"result":{{result}} }"""), metadata);
                var valid = $$"""{"{{idField}}":"stone"}""";
                var parsed = Read(valid);
                Require(parsed.Complete && parsed.Occurrences.IsEmpty, $"{major}/{shape}: valid item identifier is protected nontext");
                Require(Read($$"""{"{{idField}}":":stone"}""", ":crafting_" + shape).Complete,
                    $"{major}/{shape}: vanilla resolves leading-colon item and recipe identifiers under minecraft");
                foreach (var invalid in new[] { "{}", "null", $$"""{"{{(idField == "item" ? "id" : "item")}}":"stone"}""",
                    $$"""{"{{idField}}":"minecraft:air"}""", $$"""{"{{idField}}":":air"}""", $$"""{"{{idField}}":"Bad Id"}""" })
                {
                    var rejected = Read(invalid);
                    Require(!rejected.Complete && rejected.Diagnostics.Single().Code == "InvalidRecipeResult"
                        && rejected.Diagnostics.Single().Address.NodePath.ToJsonPointer() == "/result", $"{major}/{shape}: invalid result {invalid}");
                }
                Require(Read(valid, "probe:crafting_" + shape).Diagnostics.Single().Code == "UnsupportedPackRecipe"
                    && Read(valid, folder: directory == "recipes" ? "recipe" : "recipes").Diagnostics.Single().Code == "UnsupportedPackJson",
                    $"{major}/{shape}: unverified serializer and wrong directory remain incomplete");
                var shorthand = Read("\"stone\"");
                Require(shorthand.Complete == (major == 99) && Read("\"air\"").Diagnostics.Single().Code == "InvalidRecipeResult",
                    $"{major}/{shape}: scalar result form is version-specific and air is rejected");
                if (major != 26)
                {
                    var withName = Read("""{"id":"stone","components":{"minecraft:custom_name":{"text":"Prize"}}}""");
                    Require(withName.Complete && withName.Occurrences.Single().Text == "Prize", $"{major}/{shape}: result keeps native component text extraction");
                }
            }
        }
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
