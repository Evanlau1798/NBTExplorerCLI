using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

public static partial class PackTextExtractor
{
    private static bool NestedItemScope(TextExtractionCollector output)
        => new[] { "item", "book", "sign", "hanging_sign", "lectern.book", "container.name", "command_block.name", "command.block", "entity.name", "armor_stand.name", "text_display.text" }.Any(output.CouldInclude);
    private static void JsonItem(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> component,
        Action<JsonElement, TypedPath, string> rawComponent, CapabilityProfile profile, string bookRole = "book")
    {
        if (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any()) return;
        if (!value.TryGetNativeProperty("id", out var id) || id.ValueKind != JsonValueKind.String) { output.Diagnostic(path, "MissingItemIdentity"); return; }
        if (value.TryGetNativeProperty("components", out var components)) JsonComponents(components, At(path, "components"), output, component, rawComponent, profile, bookRole);
    }
    private static void JsonBlockEntity(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> rawComponent, CapabilityProfile profile)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            output.Merge(SnbtTextExtractor.BlockEntity(output.Address(path, "container.name"), NativeJsonStrings.Read(value), profile, output.Limits, output.Scope)); return;
        }
        if (!value.TryGetNativeProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String) { output.Diagnostic(path, "MissingBlockEntityIdentity"); return; }
        var id = MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(identity));
        Field("CustomName", id == "minecraft:command_block" ? "command_block.name" : "container.name");
        if (value.TryGetNativeProperty("Items", out var items))
            foreach (var (item, itemPath) in Elements(items, At(path, "Items"), output)) JsonItem(item, itemPath, output, rawComponent, rawComponent, profile);
        if (id is "minecraft:sign" or "minecraft:hanging_sign")
        {
            var role = id == "minecraft:sign" ? "sign" : "hanging_sign";
            foreach (var side in new[] { "front", "back" })
            {
                if (!value.TryGetNativeProperty(side + "_text", out var text)) continue;
                var sidePath = At(path, side + "_text");
                foreach (var field in new[] { "messages", "filtered_messages" })
                    if (text.TryGetNativeProperty(field, out var messages) && output.CouldInclude(role + "." + side + ".line"))
                        foreach (var (line, linePath) in Elements(messages, At(sidePath, field), output)) rawComponent(line, linePath, role + "." + side + ".line");
            }
        }
        if (id == "minecraft:lectern" && value.TryGetNativeProperty("Book", out var book)) JsonItem(book, At(path, "Book"), output, rawComponent, rawComponent, profile, "lectern.book");
        if (id == "minecraft:command_block" && value.TryGetNativeProperty("Command", out var command))
        {
            if (command.ValueKind == JsonValueKind.String) output.Command(At(path, "Command"), NativeJsonStrings.Read(command));
            else output.Diagnostic(At(path, "Command"), "InvalidCommand");
        }
        void Field(string field, string role)
        {
            if (value.TryGetNativeProperty(field, out var text)) rawComponent(text, At(path, field), role);
        }
    }
    private static void JsonEntity(JsonElement value, TypedPath path, TextExtractionCollector output, Action<JsonElement, TypedPath, string> rawComponent, CapabilityProfile profile)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            output.Merge(SnbtTextExtractor.Entity(output.Address(path, "entity.name"), NativeJsonStrings.Read(value), profile, output.Limits, output.Scope)); return;
        }
        if (!value.TryGetNativeProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String) { output.Diagnostic(path, "MissingEntityIdentity"); return; }
        var id = MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(identity));
        if (value.TryGetNativeProperty("CustomName", out var name)) rawComponent(name, At(path, "CustomName"), id == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name");
        if (id == "minecraft:text_display" && value.TryGetNativeProperty("text", out var display)) rawComponent(display, At(path, "text"), "text_display.text");
        foreach (var field in new[] { "Inventory", "EnderItems", "Items" })
            if (value.TryGetNativeProperty(field, out var items))
                foreach (var (item, itemPath) in Elements(items, At(path, field), output)) JsonItem(item, itemPath, output, rawComponent, rawComponent, profile);
        if (value.TryGetNativeProperty("Item", out var carried)) JsonItem(carried, At(path, "Item"), output, rawComponent, rawComponent, profile);
        if (id == "minecraft:item_display" && value.TryGetNativeProperty("item", out var displayed)) JsonItem(displayed, At(path, "item"), output, rawComponent, rawComponent, profile);
        if (profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt")
        {
            if (value.TryGetNativeProperty("equipment", out var equipment))
                foreach (var slot in new[] { "head", "chest", "legs", "feet", "mainhand", "offhand", "body", "saddle" })
                    if (equipment.TryGetNativeProperty(slot, out var item)) JsonItem(item, At(At(path, "equipment"), slot), output, rawComponent, rawComponent, profile);
        }
        else
        {
            foreach (var field in new[] { "HandItems", "ArmorItems" })
                if (value.TryGetNativeProperty(field, out var items))
                    foreach (var (item, itemPath) in Elements(items, At(path, field), output)) JsonItem(item, itemPath, output, rawComponent, rawComponent, profile);
            foreach (var field in new[] { "ArmorItem", "body_armor_item", "SaddleItem", "DecorItem" })
                if (value.TryGetNativeProperty(field, out var item)) JsonItem(item, At(path, field), output, rawComponent, rawComponent, profile);
        }
        if (value.TryGetNativeProperty("Passengers", out var passengers))
            foreach (var (passenger, passengerPath) in Elements(passengers, At(path, "Passengers"), output)) JsonEntity(passenger, passengerPath, output, rawComponent, profile);
    }
}
