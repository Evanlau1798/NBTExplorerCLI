using System.Collections.Frozen;
using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class NbtTextReader
{
    private static readonly FrozenSet<string> Java1204SpawnEggEntities = new[]
    {
        "allay", "axolotl", "bat", "bee", "blaze", "breeze",
        "camel", "cat", "cave_spider", "chicken", "cod", "cow",
        "creeper", "dolphin", "donkey", "drowned", "elder_guardian", "ender_dragon",
        "enderman", "endermite", "evoker", "fox", "frog", "ghast",
        "glow_squid", "goat", "guardian", "hoglin", "horse", "husk",
        "iron_golem", "llama", "magma_cube", "mooshroom", "mule", "ocelot",
        "panda", "parrot", "phantom", "pig", "piglin", "piglin_brute",
        "pillager", "polar_bear", "pufferfish", "rabbit", "ravager", "salmon",
        "sheep", "shulker", "silverfish", "skeleton", "skeleton_horse", "slime",
        "sniffer", "snow_golem", "spider", "squid", "stray", "strider",
        "tadpole", "trader_llama", "tropical_fish", "turtle", "vex", "villager",
        "vindicator", "wandering_trader", "warden", "witch", "wither", "wither_skeleton",
        "wolf", "zoglin", "zombie", "zombie_horse", "zombie_villager", "zombified_piglin"
    }.ToFrozenSet(StringComparer.Ordinal);

    internal void Item(NbtValue value, TypedPath path, string bookRole = "book")
    {
        if (value is NbtCompound { Entries.Length: 0 }) return;
        var identity = Field(value, "id", path);
        if (classic && identity is NbtShort or NbtInt or NbtString)
        {
            var id = identity switch
            {
                NbtShort { Value: 386 } or NbtInt { Value: 386 } => "minecraft:writable_book",
                NbtShort { Value: 387 } or NbtInt { Value: 387 } => "minecraft:written_book",
                NbtString text => MinecraftIdentifier.DefaultNamespace(text.Value),
                _ => null
            };
            if (Field(value, "tag", path) is { } tag) TaggedItem(tag, At(path, "tag"), id, bookRole);
            return;
        }
        if (identity is not NbtString named) { output.Diagnostic(path, "MissingItemIdentity"); return; }
        var oldTags = profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4";
        var field = oldTags ? "tag" : "components";
        if (Field(value, field, path) is not { } data) return;
        var dataPath = At(path, field);
        if (data is not NbtCompound) { output.Diagnostic(dataPath, "InvalidItemData"); return; }
        if (oldTags) { TaggedItem(data, dataPath, MinecraftIdentifier.DefaultNamespace(named.Value), bookRole); return; }
        foreach (var group in ((NbtCompound)data).Entries.GroupBy(entry => MinecraftIdentifier.DefaultNamespace(entry.Name), StringComparer.Ordinal))
        {
            if (group.Count() != 1) { output.Diagnostic(dataPath, "AmbiguousItemComponent"); continue; }
            var entry = group.Single(); var componentPath = At(dataPath, entry.Name);
            ItemComponent(group.Key, entry.Value, componentPath, bookRole);
        }
    }
    internal void ItemComponent(string name, NbtValue value, TypedPath path, string bookRole = "book")
    {
            switch (name)
            {
                case "minecraft:custom_name": case "minecraft:item_name": Component(value, path, "item.name"); break;
                case "minecraft:lore":
                    foreach (var (lore, lorePath) in Sequence(value, path)) Component(lore, lorePath, "item.lore"); break;
                case "minecraft:container":
                    foreach (var (slot, slotPath) in Sequence(value, path))
                        if (Field(slot, "item", slotPath) is { } nested) Item(nested, At(slotPath, "item")); break;
                case "minecraft:bundle_contents": case "minecraft:charged_projectiles":
                    foreach (var (nested, nestedPath) in Sequence(value, path)) Item(nested, nestedPath); break;
                case "minecraft:block_entity_data": BlockEntity(value, path); break;
                case "minecraft:entity_data": Entity(value, path); break;
                case "minecraft:written_book_content": Book(value, path, bookRole, written: true); break;
                case "minecraft:writable_book_content": Book(value, path, bookRole, written: false); break;
            }
    }
    internal void ItemTag(NbtValue tag, string? itemId) => TaggedItem(tag, TypedPath.Root, itemId, "book");
    private void TaggedItem(NbtValue tag, TypedPath path, string? id, string bookRole)
    {
        var items = id == "minecraft:bundle" ? "Items" : id == "minecraft:crossbow" ? "ChargedProjectiles" : null;
        if (items is not null)
            foreach (var (nested, nestedPath) in Sequence(Field(tag, items, path), At(path, items))) Item(nested, nestedPath);
        if (Field(tag, "display", path) is { } display)
        {
            var displayPath = At(path, "display");
            Component(Field(display, "Name", displayPath), At(displayPath, "Name"), "item.name");
            foreach (var (lore, lorePath) in Sequence(Field(display, "Lore", displayPath), At(displayPath, "Lore"))) Component(lore, lorePath, "item.lore");
        }
        if (Field(tag, "BlockEntityTag", path) is { } block && !(id == "minecraft:shield" && ShieldPatterns(block)))
            BlockEntity(block, At(path, "BlockEntityTag"), fallbackIdentity: TaggedBlockIdentity(id));
        if (Field(tag, "EntityTag", path) is { } entity)
        {
            // Tagged vanilla spawn eggs use their registered entity when EntityTag omits id.
            const string vanilla = "minecraft:";
            const string suffix = "_spawn_egg";
            var name = profile.Id is ("java-1.20.1" or "java-1.20.4") && id is not null && id.StartsWith(vanilla, StringComparison.Ordinal) && id.EndsWith(suffix, StringComparison.Ordinal)
                ? id[vanilla.Length..^suffix.Length] : null;
            var fallback = id == "minecraft:painting" ? id
                : name is not null && Java1204SpawnEggEntities.Contains(name) && (name != "breeze" || profile.Id == "java-1.20.4") ? vanilla + name : null;
            Entity(entity, At(path, "EntityTag"), fallbackIdentity: fallback);
        }
        if (id is null && tag is NbtCompound compound && compound.Entries.Any(entry => entry.Name switch
        {
            "pages" or "filtered_pages" => output.CouldInclude(bookRole + ".page"),
            "title" or "filtered_title" => output.CouldInclude(bookRole + ".title"),
            "author" => output.CouldInclude(bookRole + ".author"),
            "Items" or "ChargedProjectiles" => new[] { "item", "book", "container.name", "sign", "hanging_sign", "lectern.book", "command_block.name", "command.block", "entity.name", "armor_stand.name", "text_display.text" }.Any(output.CouldInclude),
            _ => false
        }))
            output.Diagnostic(path, "UnknownTaggedItemType");
        if (id is not ("minecraft:written_book" or "minecraft:writable_book")) return;
        var written = id == "minecraft:written_book";
        Literal(Field(tag, "title", path), At(path, "title"), bookRole + ".title");
        Literal(Field(tag, "filtered_title", path), At(path, "filtered_title"), bookRole + ".title");
        Literal(Field(tag, "author", path), At(path, "author"), bookRole + ".author");
        foreach (var (page, pagePath) in Sequence(Field(tag, "pages", path), At(path, "pages")))
            if (written) Component(page, pagePath, bookRole + ".page", classicEncoded: true); else Literal(page, pagePath, bookRole + ".page");
        if (Field(tag, "filtered_pages", path) is NbtCompound filtered)
            foreach (var entry in filtered.Entries)
                if (written) Component(entry.Value, At(At(path, "filtered_pages"), entry.Name), bookRole + ".page", classicEncoded: true); else Literal(entry.Value, At(At(path, "filtered_pages"), entry.Name), bookRole + ".page");
    }
    private static string? TaggedBlockIdentity(string? item) => item switch
    {
        "minecraft:chest" => "minecraft:chest",
        "minecraft:spawner" => "minecraft:mob_spawner",
        "minecraft:white_banner" or "minecraft:orange_banner" or "minecraft:magenta_banner" or "minecraft:light_blue_banner"
            or "minecraft:yellow_banner" or "minecraft:lime_banner" or "minecraft:pink_banner" or "minecraft:gray_banner"
            or "minecraft:light_gray_banner" or "minecraft:cyan_banner" or "minecraft:purple_banner" or "minecraft:blue_banner"
            or "minecraft:brown_banner" or "minecraft:green_banner" or "minecraft:red_banner" or "minecraft:black_banner" => "minecraft:banner",
        _ => null
    };
    private static bool ShieldPatterns(NbtValue value)
        => value is NbtCompound tag && tag.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() == tag.Entries.Length
            && tag.Entries.All(entry => entry switch
            {
                { Name: "Base", Value: NbtInt } => true,
                { Name: "Patterns", Value: NbtList list } => list.Items.All(item => item is NbtCompound { Entries.Length: 2 } pattern
                    && pattern.Entries.Any(field => field is { Name: "Color", Value: NbtInt })
                    && pattern.Entries.Any(field => field is { Name: "Pattern", Value: NbtString })),
                _ => false
            });
    private void Book(NbtValue value, TypedPath path, string role, bool written)
    {
        if (written)
        {
            if (Field(value, "title", path) is { } title) Filtered(title, At(path, "title"), role + ".title", component: false);
            Literal(Field(value, "author", path), At(path, "author"), role + ".author");
        }
        foreach (var (page, pagePath) in Sequence(Field(value, "pages", path), At(path, "pages"))) Filtered(page, pagePath, role + ".page", component: written);
    }
    private void Filtered(NbtValue value, TypedPath path, string role, bool component)
    {
        if (value is NbtCompound compound && compound.Entries.Any(entry => entry.Name is "raw" or "filtered"))
        {
            if (Field(value, "raw", path) is null) output.Diagnostic(path, "MissingRawFilteredText");
            foreach (var field in new[] { "raw", "filtered" })
                if (Field(value, field, path) is { } text)
                    if (component) Component(text, At(path, field), role); else Literal(text, At(path, field), role);
        }
        else if (component) Component(value, path, role); else Literal(value, path, role);
    }
}
