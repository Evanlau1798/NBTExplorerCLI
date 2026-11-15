using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

public static partial class PackTextExtractor
{
    private static void Loot(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component, CapabilityProfile profile)
    {
        if (value.ValueKind != JsonValueKind.Object) { output.Diagnostic(path, "InvalidLootTable"); return; }
        if (value.TryGetNativeProperty("functions", out var functions)) Modifier(functions, At(path, "functions"), output, component, profile);
        if (!value.TryGetNativeProperty("pools", out var pools)) return;
        foreach (var (pool, poolPath) in Elements(pools, At(path, "pools"), output))
        {
            if (pool.TryGetNativeProperty("functions", out var modifiers)) Modifier(modifiers, At(poolPath, "functions"), output, component, profile);
            if (pool.TryGetNativeProperty("entries", out var entries))
                foreach (var (entry, entryPath) in Elements(entries, At(poolPath, "entries"), output)) LootEntry(entry, entryPath, output, component, profile);
        }
    }
    private static void LootEntry(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component, CapabilityProfile profile)
    {
        if (value.ValueKind != JsonValueKind.Object) { output.Diagnostic(path, "InvalidLootEntry"); return; }
        if (value.TryGetNativeProperty("functions", out var functions)) Modifier(functions, At(path, "functions"), output, component, profile);
        if (value.TryGetNativeProperty("children", out var children))
            foreach (var (entry, entryPath) in Elements(children, At(path, "children"), output)) LootEntry(entry, entryPath, output, component, profile);
        if (value.TryGetNativeProperty("type", out var type) && type.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(type) is "minecraft:loot_table" or "loot_table"
            && value.TryGetNativeProperty("value", out var inline) && inline.ValueKind == JsonValueKind.Object) Loot(inline, At(path, "value"), output, component, profile);
    }
    private static void Modifier(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component, CapabilityProfile profile)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var (function, functionPath) in Elements(value, path, output)) Modifier(function, functionPath, output, component, profile);
            return;
        }
        if (value.ValueKind == JsonValueKind.String) return; // Named modifier references contain no literal display text.
        if (!value.TryGetNativeProperty("function", out var type) || type.ValueKind != JsonValueKind.String)
        { output.Diagnostic(path, "InvalidLootFunction"); return; }
        var id = NativeJsonStrings.Read(type);
        if (!LootFunctionProfiles.IsKnown(profile, id)) { output.Diagnostic(path, "UnknownLootFunction"); return; }
        if (!id.Contains(':')) id = "minecraft:" + id;
        switch (id)
        {
            case "minecraft:set_name":
                if (value.TryGetNativeProperty("name", out var name)) component(name, At(path, "name"), "item.name");
                break;
            case "minecraft:set_lore":
                if (value.TryGetNativeProperty("lore", out var lore))
                    foreach (var (line, linePath) in Elements(lore, At(path, "lore"), output)) component(line, linePath, "item.lore");
                break;
            case "minecraft:set_written_book_pages":
            case "minecraft:set_writable_book_pages":
                if (value.TryGetNativeProperty("pages", out var pages))
                    foreach (var (page, pagePath) in Elements(pages, At(path, "pages"), output))
                        FilteredJson(page, pagePath, "book.page", output, id == "minecraft:set_written_book_pages" ? component : null);
                break;
            case "minecraft:set_book_cover":
                if (value.TryGetNativeProperty("title", out var title)) FilteredJson(title, At(path, "title"), "book.title", output);
                if (value.TryGetNativeProperty("author", out var author)) PlainJson(author, At(path, "author"), "book.author", output);
                break;
            case "minecraft:set_components":
                if (value.TryGetNativeProperty("components", out var components)) JsonComponents(components, At(path, "components"), output, component);
                break;
            case "minecraft:sequence":
                if (value.TryGetNativeProperty("functions", out var sequence)) Modifier(sequence, At(path, "functions"), output, component, profile);
                break;
            case "minecraft:filtered":
            case "minecraft:modify_contents":
                if (value.TryGetNativeProperty("modifier", out var nested)) Modifier(nested, At(path, "modifier"), output, component, profile);
                break;
            case "minecraft:set_contents":
                if (value.TryGetNativeProperty("entries", out var entries))
                    foreach (var (entry, entryPath) in Elements(entries, At(path, "entries"), output)) LootEntry(entry, entryPath, output, component, profile);
                break;
            case "minecraft:set_nbt": output.Diagnostic(path, "UnsupportedLegacyTagSyntax"); break;
        }
    }
    private static void JsonComponents(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component)
    {
        if (value.ValueKind != JsonValueKind.Object) { output.Diagnostic(path, "InvalidItemComponents"); return; }
        foreach (var group in value.EnumerateObject().GroupBy(property => MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.PropertyName(property)), StringComparer.Ordinal))
        {
            if (group.Count() != 1) { output.Diagnostic(path, "AmbiguousItemComponent"); continue; }
            var property = group.Single(); var componentPath = At(path, NativeJsonStrings.PropertyName(property));
            switch (group.Key)
            {
                case "minecraft:custom_name": case "minecraft:item_name": component(property.Value, componentPath, "item.name"); break;
                case "minecraft:lore":
                    if (output.CouldInclude("item.lore"))
                        foreach (var (line, linePath) in Elements(property.Value, componentPath, output)) component(line, linePath, "item.lore"); break;
                case "minecraft:written_book_content": JsonBook(property.Value, componentPath, true, output, component); break;
                case "minecraft:writable_book_content": JsonBook(property.Value, componentPath, false, output, component); break;
                case "minecraft:container": case "minecraft:bundle_contents": case "minecraft:charged_projectiles":
                case "minecraft:block_entity_data": case "minecraft:entity_data":
                    // shortcut: these native JSON item schemas need their own paths before C02 can be accepted.
                    if (new[] { "item", "book", "sign", "hanging_sign", "lectern.book", "container.name", "command_block.name", "command.block", "entity.name", "armor_stand.name", "text_display.text" }.Any(output.CouldInclude))
                        output.Diagnostic(componentPath, "UnsupportedNestedItemData");
                    break;
            }
        }
    }
    private static void JsonBook(JsonElement book, TypedPath path, bool written, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component)
    {
        if (!output.CouldInclude("book")) return;
        if (book.ValueKind != JsonValueKind.Object) { output.Diagnostic(path, "InvalidBookContent"); return; }
        if (written)
        {
            if (book.TryGetNativeProperty("title", out var title)) FilteredJson(title, At(path, "title"), "book.title", output);
            if (book.TryGetNativeProperty("author", out var author)) PlainJson(author, At(path, "author"), "book.author", output);
        }
        if (book.TryGetNativeProperty("pages", out var pages))
            foreach (var (page, pagePath) in Elements(pages, At(path, "pages"), output)) FilteredJson(page, pagePath, "book.page", output, written ? component : null);
    }
    private static void FilteredJson(JsonElement value, TypedPath path, string role, TextExtractionCollector output, Action<JsonElement, TypedPath, string>? component = null)
    {
        if (value.ValueKind == JsonValueKind.Object && (value.TryGetNativeProperty("raw", out _) || value.TryGetNativeProperty("filtered", out _)))
        {
            if (!value.TryGetNativeProperty("raw", out _)) output.Diagnostic(path, "MissingRawFilteredText");
            foreach (var member in new[] { "raw", "filtered" })
                if (value.TryGetNativeProperty(member, out var text))
                    if (component is null) PlainJson(text, At(path, member), role, output); else component(text, At(path, member), role);
        }
        else if (component is null) PlainJson(value, path, role, output); else component(value, path, role);
    }
    private static void PlainJson(JsonElement value, TypedPath path, string role, TextExtractionCollector output)
    {
        if (!output.Includes(role)) return;
        if (value.ValueKind == JsonValueKind.String) output.Literal(path, NativeJsonStrings.Read(value), role);
        else output.Diagnostic(path, "InvalidString");
    }
    private static IEnumerable<(JsonElement Value, TypedPath Path)> Elements(JsonElement value, TypedPath path, TextExtractionCollector output)
    {
        if (value.ValueKind != JsonValueKind.Array) { output.Diagnostic(path, "InvalidLootList"); yield break; }
        var index = 0; foreach (var element in value.EnumerateArray()) yield return (element, At(path, index++));
    }
}
