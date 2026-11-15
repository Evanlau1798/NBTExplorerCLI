using Next.Domain;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

internal sealed partial class NbtTextReader
{
    internal void Structure(NbtValue root)
    {
        foreach (var (block, path) in Sequence(Field(root, "blocks", TypedPath.Root), At(TypedPath.Root, "blocks")))
            if (Field(block, "nbt", path) is { } data) BlockEntity(data, At(path, "nbt"));
        foreach (var (entity, path) in Sequence(Field(root, "entities", TypedPath.Root), At(TypedPath.Root, "entities")))
            if (Field(entity, "nbt", path) is { } data) Entity(data, At(path, "nbt"));
    }
    internal void Terrain(NbtValue root)
    {
        foreach (var (block, path) in Sequence(Field(root, "block_entities", TypedPath.Root), At(TypedPath.Root, "block_entities"))) BlockEntity(block, path);
    }
    internal void Entities(NbtValue root)
    {
        foreach (var (entity, path) in Sequence(Field(root, "Entities", TypedPath.Root), At(TypedPath.Root, "Entities"))) Entity(entity, path);
    }
    internal void BlockEntity(NbtValue value, TypedPath path)
    {
        if (Field(value, "id", path) is not NbtString id) { output.Diagnostic(path, "MissingBlockEntityIdentity"); return; }
        var identity = MinecraftIdentifier.DefaultNamespace(id.Value);
        var role = identity == "minecraft:command_block" ? "command_block.name" : "container.name";
        Component(Field(value, "CustomName", path), At(path, "CustomName"), role);
        foreach (var (item, itemPath) in Sequence(Field(value, "Items", path), At(path, "Items"))) Item(item, itemPath);
        if (identity is "minecraft:sign" or "minecraft:hanging_sign")
        {
            var signRole = identity == "minecraft:sign" ? "sign" : "hanging_sign";
            foreach (var side in new[] { "front", "back" })
            {
                var sideField = side + "_text";
                if (Field(value, sideField, path) is not { } text) continue;
                var sidePath = At(path, sideField);
                foreach (var field in new[] { "messages", "filtered_messages" })
                    foreach (var (line, linePath) in Sequence(Field(text, field, sidePath), At(sidePath, field))) Component(line, linePath, signRole + "." + side + ".line");
            }
        }
        if (identity == "minecraft:lectern" && Field(value, "Book", path) is { } book) Item(book, At(path, "Book"), "lectern.book");
        if (identity == "minecraft:command_block" && Field(value, "Command", path) is { } command)
        {
            if (command is NbtString text) output.Command(At(path, "Command"), text.Value);
            else output.Diagnostic(At(path, "Command"), "InvalidCommand");
        }
    }
    internal void Entity(NbtValue value, TypedPath path)
    {
        if (Field(value, "id", path) is not NbtString id) { output.Diagnostic(path, "MissingEntityIdentity"); return; }
        var identity = MinecraftIdentifier.DefaultNamespace(id.Value);
        Component(Field(value, "CustomName", path), At(path, "CustomName"), identity == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name");
        if (identity == "minecraft:text_display") Component(Field(value, "text", path), At(path, "text"), "text_display.text");
        Player(value, path);
        foreach (var (item, itemPath) in Sequence(Field(value, "Items", path), At(path, "Items"))) Item(item, itemPath);
        if (Field(value, "Item", path) is { } carried) Item(carried, At(path, "Item"));
        if (identity == "minecraft:item_display" && Field(value, "item", path) is { } displayed) Item(displayed, At(path, "item"));
        foreach (var (passenger, passengerPath) in Sequence(Field(value, "Passengers", path), At(path, "Passengers"))) Entity(passenger, passengerPath);
    }
    private void Equipment(NbtValue value, TypedPath path)
    {
        if (modern)
        {
            if (Field(value, "equipment", path) is not { } equipment) return;
            var equipmentPath = At(path, "equipment");
            foreach (var slot in new[] { "head", "chest", "legs", "feet", "mainhand", "offhand", "body", "saddle" })
                if (Field(equipment, slot, equipmentPath) is { } item) Item(item, At(equipmentPath, slot));
        }
        else
        {
            foreach (var field in new[] { "HandItems", "ArmorItems" })
                foreach (var (item, itemPath) in Sequence(Field(value, field, path), At(path, field))) Item(item, itemPath);
            foreach (var field in new[] { "ArmorItem", "body_armor_item", "SaddleItem", "DecorItem" })
                if (Field(value, field, path) is { } item) Item(item, At(path, field));
        }
    }
    internal void Level(NbtValue root)
    {
        if (Field(root, "Data", TypedPath.Root) is not { } data) return;
        var path = At(TypedPath.Root, "Data");
        if (Field(data, "Player", path) is { } player) Player(player, At(path, "Player"));
        if (Field(data, "CustomBossEvents", path) is { } bosses) Bossbars(bosses, At(path, "CustomBossEvents"));
    }
    internal void SavedData(NbtValue root, string artifact, IReadOnlyList<StorageTextRule>? storageRules)
    {
        var filename = artifact[(artifact.LastIndexOf('/') + 1)..];
        if (!VanillaSavedDataPath(artifact, filename)) { output.Diagnostic(TypedPath.Root, "UnsupportedSavedData"); return; }
        if (Field(root, "data", TypedPath.Root) is not { } data) { output.Diagnostic(TypedPath.Root, "MissingSavedDataRoot"); return; }
        var path = At(TypedPath.Root, "data");
        if (filename.StartsWith("command_storage_", StringComparison.Ordinal) && filename.EndsWith(".dat", StringComparison.Ordinal))
        {
            if (Field(data, "contents", path) is { } contents) Storage(root, contents, At(path, "contents"), storageRules ?? []);
            else output.Diagnostic(path, "MissingStorageContents");
        }
        else if (filename == "custom_boss_events.dat") Bossbars(data, path);
        else if (filename == "scoreboard.dat")
        {
            foreach (var (objective, objectivePath) in Sequence(Field(data, "Objectives", path), At(path, "Objectives")))
                Component(Field(objective, "DisplayName", objectivePath), At(objectivePath, "DisplayName"), "scoreboard.objective.name");
            foreach (var (team, teamPath) in Sequence(Field(data, "Teams", path), At(path, "Teams")))
            {
                Component(Field(team, "DisplayName", teamPath), At(teamPath, "DisplayName"), "scoreboard.team.name");
                Component(Field(team, "MemberNamePrefix", teamPath), At(teamPath, "MemberNamePrefix"), "scoreboard.team.prefix");
                Component(Field(team, "MemberNameSuffix", teamPath), At(teamPath, "MemberNameSuffix"), "scoreboard.team.suffix");
            }
        }
        else if (!KnownNonTextData(artifact, filename)) output.Diagnostic(TypedPath.Root, "UnsupportedSavedData");
    }
    private void Bossbars(NbtValue value, TypedPath path)
    {
        if (value is not NbtCompound bosses) { output.Diagnostic(path, "InvalidBossbars"); return; }
        if (bosses.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != bosses.Entries.Length)
        { output.Diagnostic(path, "AmbiguousBossbars"); return; }
        foreach (var entry in bosses.Entries) Component(Field(entry.Value, "Name", At(path, entry.Name)), At(At(path, entry.Name), "Name"), "bossbar.name");
    }
}
