using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class NbtTextReader
{
    internal void Structure(NbtValue root)
    {
        foreach (var (block, path) in Sequence(Field(root, "blocks", TypedPath.Root), At(TypedPath.Root, "blocks")))
            if (Field(block, "nbt", path) is { } data) BlockEntity(data, At(path, "nbt"));
        foreach (var (entity, path) in Sequence(Field(root, "entities", TypedPath.Root), At(TypedPath.Root, "entities")))
            if (Field(entity, "nbt", path) is { } data) Entity(data, At(path, "nbt"));
    }
    private const int FirstFlatTerrainDataVersion = 2844; // 21w43a moved Level contents to the chunk root.

    internal void Terrain(NbtValue root, int? dataVersion)
    {
        var path = TypedPath.Root;
        var nested = Field(root, "Level", path);
        var wrapped = classic || earlyAnvil || dataVersion is < FirstFlatTerrainDataVersion;
        if (wrapped)
        {
            path = At(path, "Level");
            if (nested is not NbtCompound level
                || Field(level, "xPos", path) is not NbtInt x || Field(level, "zPos", path) is not NbtInt z
                || binding.Chunk is { } chunk && (x.Value != chunk.X || z.Value != chunk.Z)
                || Field(root, "block_entities", TypedPath.Root) is not null || Field(root, "entities", TypedPath.Root) is not null)
            { output.Diagnostic(path, classic ? "InvalidClassicAnvilChunk" : "InvalidWrappedAnvilChunk"); return; }
            root = level;
        }
        else if (nested is not null)
        { output.Diagnostic(path, "InvalidFlatAnvilChunk"); return; }
        var blocks = wrapped ? "TileEntities" : "block_entities";
        foreach (var (block, blockPath) in Sequence(Field(root, blocks, path), At(path, blocks))) BlockEntity(block, blockPath);
        var entities = wrapped ? "Entities" : "entities";
        if (profile != MinecraftProfiles.Java125)
            foreach (var (entity, entityPath) in Sequence(Field(root, entities, path), At(path, entities))) Entity(entity, entityPath);
    }
    internal void Entities(NbtValue root)
    {
        foreach (var (entity, path) in Sequence(Field(root, "Entities", TypedPath.Root), At(TypedPath.Root, "Entities"))) Entity(entity, path);
    }
    internal void BlockEntity(NbtValue value, TypedPath path, string? knownIdentity = null, string? fallbackIdentity = null)
    {
        var rawIdentity = Field(value, "id", path, out var status);
        if (status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid) return;
        var identity = rawIdentity switch
        {
            NbtString id => MinecraftIdentifier.DefaultNamespace(id.Value),
            null when status == ResolutionStatus.Missing && value is NbtCompound => knownIdentity ?? fallbackIdentity,
            _ => null
        };
        if (identity is null) { output.Diagnostic(path, "MissingBlockEntityIdentity"); return; }
        if (knownIdentity is not null && identity != knownIdentity) { output.Diagnostic(path, "ConflictingBlockEntityIdentity"); return; }
        var signsOnly = profile == MinecraftProfiles.Java125;
        if (signsOnly && (identity is "minecraft:sign" or "minecraft:hanging_sign"
            || identity == "minecraft:Sign" && (Field(value, "front_text", path) is not null || Field(value, "back_text", path) is not null)))
        {
            output.Diagnostic(path, "UnsupportedClassicSignSchema");
            return;
        }
        if (!signsOnly)
        {
            var role = identity == "minecraft:command_block" || (classic || earlyAnvil) && identity == "minecraft:Control" ? "command_block.name" : "container.name";
            Component(Field(value, "CustomName", path), At(path, "CustomName"), role);
            foreach (var (item, itemPath) in Sequence(Field(value, "Items", path), At(path, "Items"))) Item(item, itemPath);
        }
        if ((classic || earlyAnvil) && identity == "minecraft:Sign")
            foreach (var field in new[] { "Text1", "Text2", "Text3", "Text4" })
                Component(Field(value, field, path), At(path, field), "sign.front.line", classicEncoded: true);
        if (signsOnly) return;
        if (profile == MinecraftProfiles.ClassicAnvil && identity == "minecraft:Control")
            Literal(Field(value, "Command", path), At(path, "Command"), "command.block.raw");
        if (identity is "minecraft:sign" or "minecraft:hanging_sign")
        {
            var signRole = identity == "minecraft:sign" ? "sign" : "hanging_sign";
            var legacyLines = new[] { "Text1", "Text2", "Text3", "Text4", "FilteredText1", "FilteredText2", "FilteredText3", "FilteredText4" };
            if (value is NbtCompound sign && sign.Entries.Any(entry => legacyLines.Contains(entry.Name)))
            {
                if (modern || identity != "minecraft:sign" || sign.Entries.Any(entry => entry.Name is "front_text" or "back_text"))
                { output.Diagnostic(path, "UnsupportedSignSchema"); return; }
                foreach (var field in legacyLines)
                    Component(Field(value, field, path), At(path, field), "sign.front.line", classicEncoded: earlyAnvil);
                return;
            }
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
        if (identity == "minecraft:command_block" || (earlyAnvil || MinecraftProfiles.IsVersionedClassic(profile)) && identity == "minecraft:Control") StoredCommand(value, path);
        if (identity == "minecraft:mob_spawner")
        {
            SpawnedEntity(value, path, "SpawnData");
            foreach (var (potential, potentialPath) in Sequence(Field(value, "SpawnPotentials", path), At(path, "SpawnPotentials")))
                SpawnedEntity(potential, potentialPath, "data");
        }
    }
    private void SpawnedEntity(NbtValue value, TypedPath path, string field)
    {
        var dataPath = At(path, field);
        if (Field(value, field, path) is { } data && Field(data, "entity", dataPath) is { } entity
            && entity is not NbtCompound { Entries.IsEmpty: true })
            Entity(entity, At(dataPath, "entity"));
    }
    private void StoredCommand(NbtValue value, TypedPath path)
    {
        if (Field(value, "Command", path) is { } command)
        {
            if (earlyAnvil && command is NbtString raw)
            {
                if (!output.CouldInclude("command.block")) return;
                var address = At(path, "Command");
                output.Literal(address, raw.Value, "command.block.raw");
                output.Diagnostic(address, "UnsupportedEarlyAnvilCommandGrammar");
            }
            else if (command is NbtString text) output.Command(At(path, "Command"), text.Value);
            else output.Diagnostic(At(path, "Command"), "InvalidCommand");
        }
    }
    internal void Entity(NbtValue value, TypedPath path, string? knownIdentity = null, string? fallbackIdentity = null)
    {
        var rawIdentity = Field(value, "id", path, out var status);
        if (status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid) return;
        var identity = rawIdentity switch
        {
            NbtString id => MinecraftIdentifier.DefaultNamespace(id.Value),
            null when status == ResolutionStatus.Missing && value is NbtCompound => knownIdentity ?? fallbackIdentity,
            _ => null
        };
        if (identity is null) { output.Diagnostic(path, "MissingEntityIdentity"); return; }
        if (knownIdentity is not null && identity != knownIdentity) { output.Diagnostic(path, "ConflictingEntityIdentity"); return; }
        var armorStand = identity == "minecraft:armor_stand" || (classic || earlyAnvil) && identity == "minecraft:ArmorStand";
        Component(Field(value, "CustomName", path), At(path, "CustomName"), armorStand ? "armor_stand.name" : "entity.name");
        if (identity == "minecraft:command_block_minecart" || (earlyAnvil || profile == MinecraftProfiles.Java181) && identity == "minecraft:MinecartCommandBlock") StoredCommand(value, path);
        if (identity == "minecraft:text_display") Component(Field(value, "text", path), At(path, "text"), "text_display.text");
        Player(value, path);
        foreach (var (item, itemPath) in Sequence(Field(value, "Items", path), At(path, "Items"))) Item(item, itemPath);
        if (Field(value, "Item", path) is { } carried) Item(carried, At(path, "Item"));
        if (identity == "minecraft:item_display" && Field(value, "item", path) is { } displayed) Item(displayed, At(path, "item"));
        foreach (var (passenger, passengerPath) in Sequence(Field(value, "Passengers", path), At(path, "Passengers"))) Entity(passenger, passengerPath);
    }
    private void Equipment(NbtValue value, TypedPath path)
    {
        if (MinecraftProfiles.UsesCompoundEquipment(profile))
        {
            if (Field(value, "equipment", path) is not { } equipment) return;
            var equipmentPath = At(path, "equipment");
            foreach (var slot in new[] { "head", "chest", "legs", "feet", "mainhand", "offhand", "body", "saddle" })
                if (Field(equipment, slot, equipmentPath) is { } item) Item(item, At(equipmentPath, slot));
        }
        else
        {
            foreach (var field in classic ? new[] { "Equipment" } : new[] { "HandItems", "ArmorItems" })
                foreach (var (item, itemPath) in Sequence(Field(value, field, path), At(path, field)))
                    if (item is not NbtCompound { Entries: [{ Name: "Count", Value: NbtByte or NbtInt }] })
                        Item(item, itemPath);
            foreach (var field in new[] { "ArmorItem", "body_armor_item", "SaddleItem", "DecorItem" })
                if (Field(value, field, path) is { } item) Item(item, At(path, field));
        }
    }
    internal void Level(NbtValue root)
    {
        if (profile == MinecraftProfiles.Java125) return;
        if (Field(root, "Data", TypedPath.Root) is not { } data) return;
        var path = At(TypedPath.Root, "Data");
        if (Field(data, "Player", path) is { } player) Player(player, At(path, "Player"));
        if (Field(data, "CustomBossEvents", path) is { } bosses) Bossbars(bosses, At(path, "CustomBossEvents"));
    }
    internal void SavedData(NbtValue root, string artifact, IReadOnlyList<StorageTextRule>? storageRules)
    {
        if (classic && WorldLayoutLocator.IsClassicChunkTickets(artifact))
        {
            if (root is not NbtCompound { Entries: [{ Name: "TicketList", Value: NbtList { Items.IsEmpty: true } }] })
                output.Diagnostic(TypedPath.Root, "UnsupportedClassicChunkTickets");
            return;
        }
        var filename = artifact[(artifact.LastIndexOf('/') + 1)..];
        var commandStorage = WorldLayoutLocator.CommandStorageNamespace(layoutProfile, artifact) is not null;
        if (!commandStorage && !VanillaSavedDataPath(artifact, filename)) { output.Diagnostic(TypedPath.Root, "UnsupportedSavedData"); return; }
        if ((classic || earlyAnvil) && artifact == "data/idcounts.dat" && root is NbtCompound counters
            && counters.Entries.All(entry => entry.Value is NbtShort)) return;
        if (Field(root, "data", TypedPath.Root) is not { } data) { output.Diagnostic(TypedPath.Root, "MissingSavedDataRoot"); return; }
        var path = At(TypedPath.Root, "data");
        if (profile == MinecraftProfiles.Java125)
        {
            if (artifact != "data/" + filename || !ClassicNonTextData(filename, data, path))
                output.Diagnostic(TypedPath.Root, "UnsupportedSavedData");
            return;
        }
        if (commandStorage || filename.StartsWith("command_storage_", StringComparison.Ordinal) && filename.EndsWith(".dat", StringComparison.Ordinal))
        {
            if (Field(data, "contents", path) is { } contents) Storage(root, contents, At(path, "contents"), storageRules ?? []);
            else output.Diagnostic(path, "MissingStorageContents");
        }
        else if (filename == "custom_boss_events.dat") Bossbars(data, path);
        else if (filename == "scoreboard.dat")
        {
            var plain = earlyAnvil || MinecraftProfiles.UsesClassicPlainText(profile);
            void ScoreboardText(NbtValue? value, TypedPath field, string role)
            {
                if (plain) Literal(value, field, role);
                else Component(value, field, role);
            }
            foreach (var (objective, objectivePath) in Sequence(Field(data, "Objectives", path), At(path, "Objectives")))
                ScoreboardText(Field(objective, "DisplayName", objectivePath), At(objectivePath, "DisplayName"), "scoreboard.objective.name");
            var prefix = plain ? "Prefix" : "MemberNamePrefix";
            var suffix = plain ? "Suffix" : "MemberNameSuffix";
            foreach (var (team, teamPath) in Sequence(Field(data, "Teams", path), At(path, "Teams")))
            {
                ScoreboardText(Field(team, "DisplayName", teamPath), At(teamPath, "DisplayName"), "scoreboard.team.name");
                ScoreboardText(Field(team, prefix, teamPath), At(teamPath, prefix), "scoreboard.team.prefix");
                ScoreboardText(Field(team, suffix, teamPath), At(teamPath, suffix), "scoreboard.team.suffix");
            }
        }
        else if (!(!classic && !earlyAnvil && MapFilename(filename) && ReadMapData(data, path))
            && !(artifact == "data/" + filename && ClassicNonTextData(filename, data, path))
            && !KnownNonTextData(artifact, filename)) output.Diagnostic(TypedPath.Root, "UnsupportedSavedData");
    }
    private bool ReadMapData(NbtValue data, TypedPath path)
    {
        var dimension = Field(data, "dimension", path);
        if (dimension is not (NbtByte or NbtInt) && (dimension is not NbtString id || !MinecraftIdentifier.ResourceLocation(id.Value))
            || Field(data, "xCenter", path) is not NbtInt || Field(data, "zCenter", path) is not NbtInt
            || Field(data, "scale", path) is not NbtByte || Field(data, "colors", path) is not NbtByteArray) return false;
        foreach (var (banner, bannerPath) in Sequence(Field(data, "banners", path), At(path, "banners")))
        {
            var position = Field(banner, "pos", bannerPath);
            var oldPosition = Field(banner, "Pos", bannerPath);
            var lower = position is not null;
            var validPosition = lower ? oldPosition is null && position is NbtIntArray { Values.Length: 3 }
                : oldPosition is NbtCompound && Field(oldPosition, "X", At(bannerPath, "Pos")) is NbtInt
                    && Field(oldPosition, "Y", At(bannerPath, "Pos")) is NbtInt && Field(oldPosition, "Z", At(bannerPath, "Pos")) is NbtInt;
            var color = Field(banner, lower ? "color" : "Color", bannerPath);
            var name = lower ? "name" : "Name";
            if (!validPosition || color is not NbtString dye || !MinecraftIdentifier.ResourceLocation(dye.Value)
                || Field(banner, lower ? "Name" : "name", bannerPath) is not null)
                output.Diagnostic(bannerPath, "UnsupportedMapBannerSchema");
            else Component(Field(banner, name, bannerPath), At(bannerPath, name), "map.banner.name");
        }
        return true;
    }
    private void Bossbars(NbtValue value, TypedPath path)
    {
        if (value is not NbtCompound bosses) { output.Diagnostic(path, "InvalidBossbars"); return; }
        if (bosses.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != bosses.Entries.Length)
        { output.Diagnostic(path, "AmbiguousBossbars"); return; }
        foreach (var entry in bosses.Entries) Component(Field(entry.Value, "Name", At(path, entry.Name)), At(At(path, entry.Name), "Name"), "bossbar.name", classicEncoded: earlyAnvil);
    }
}
