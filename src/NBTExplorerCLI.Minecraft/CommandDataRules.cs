using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private const int StorageDataVersion = MinecraftProfiles.CommandStorageDataVersion;
    private const int DisplayEntityDataVersion = 3326;
    private const int VanillaArmorTrimDataVersion = 3442;

    private void DataCommand(List<CommandToken> tokens, int index, TypedPath path, int end, bool resultObserved)
    {
        if (ReadIndexedBlankWrite(tokens, index, path)
            || resultObserved && ReadScratchComparison(tokens, index, path)
            || ReadStorageBlankReset(tokens, index, path)) return;
        var storageElement = !resultObserved && (storageAccess is not null || storageDisplayLists is { Count: > 0 }) ? StorageDisplayWrite(tokens, index) : null;
        var storageValue = storageElement is null ? null : Snbt.ParseDocument(Utf8.GetBytes(tokens[^1].Text), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
        Dictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral>? glyphs = null;
        if (storageValue is not null && JsonStorageRuntime())
        {
            glyphs = StorageGlyphs(storageValue, tokens[index + 5].Text == "set" && tokens[^1].Text.StartsWith('['));
            if (glyphs is null) storageElement = null;
        }
        var staticListWrite = storageValue is not null && StaticDataWrite(tokens, index, storageValue, keepEmptyListReset: true);
        if (storageAccess is not null || opaqueEquipment is not null)
            ObserveDataStorage(tokens, index, path, end, staticListWrite ? null : storageElement, storageValue, resultObserved, glyphs);
        if (staticListWrite || ReadStorageDisplayWrite(tokens, index, path, storageElement, glyphs)) return;
        if (DataControl(tokens, index)) return;
        if (SignMessageWrite(tokens, index, path)) return;
        var entityWrite = tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "entity"
            && EntityTarget(tokens[index + 3].Text) && tokens[index + 5].Text == "set" && tokens[index + 6].Text == "value";
        var entityType = entityWrite ? selectorTypes.GetValueOrDefault(tokens[index + 3].Start) : null;
        var member = entityWrite ? tokens[index + 4].Text : null;
        var componentRole = member switch
        {
            "CustomName" => entityType == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name",
            "text" when entityType == "minecraft:text_display" && runtimeDataVersion >= DisplayEntityDataVersion => "text_display.text",
            _ => null
        };
        if (componentRole is not null)
        {
            var value = tokens[index + 7];
            Component(value.Start, value.Start + value.Text.Length, At(path, "data.entity." + member), storedRole: componentRole);
            return;
        }
        var blockItems = tokens.Count == index + 10 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "block"
            && BlockCoordinates(tokens[index + 3].Text, tokens[index + 4].Text, tokens[index + 5].Text)
            && tokens[index + 6].Text == "Items" && tokens[index + 7].Text is "append" or "prepend" && tokens[index + 8].Text == "value";
        var entityItem = member == "Item" && entityType is "minecraft:item" or "minecraft:item_frame" or "minecraft:glow_item_frame"
            || member == "item" && entityType == "minecraft:item_display" && runtimeDataVersion >= DisplayEntityDataVersion;
        if (blockItems || entityItem)
        {
            var token = tokens[^1]; var grammarPath = At(path, blockItems ? "data.block.Items" : "data.entity." + member);
            var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, "item.stack", source.Address.Dimension, source.Address.Chunk);
            var tokenEnd = token.Start + token.Text.Length;
            var result = SnbtTextExtractor.ItemStack(binding, token.Text, profile, limits, scope, StoredComponentCallback(grammarPath, token.Start, tokenEnd));
            MergeComponent(result, grammarPath, token.Start, tokenEnd, true);
            if (result.Diagnostics.IsEmpty) ComponentReferences(Utf8.GetBytes(token.Text), token.Start, tokenEnd, grammarPath, true, itemTree: true, commandTag: true);
            return;
        }
        if (StaticDataWrite(tokens, index, storageValue, resultObserved: resultObserved)) return;
        if (!resultObserved && ResourceStorageLabel(tokens, index, storageValue)) return;
        if (tokens.Count >= index + 5 && tokens[index + 1].Text == "merge" && tokens[index + 2].Text == "entity")
        {
            if (tokens.Count != index + 5) throw new CommandSyntaxException();
            if (selectorTypes.GetValueOrDefault(tokens[index + 3].Start) is { } identity)
            {
                Structured(tokens[index + 4].Start, tokens[index + 4].Start + tokens[index + 4].Text.Length, At(path, "data.entity.nbt"), identity, false); return;
            }
            var metadata = Snbt.ParseDocument(Utf8.GetBytes(tokens[index + 4].Text), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
            if (StaticNbtMatch.Entity(metadata)) return;
        }
        Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownDataTextSchema");
    }
    private bool SignMessageWrite(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (tokens.Count != index + 10 || tokens[index + 1].Text != "modify" || tokens[index + 2].Text != "block"
            || DataTargetEnd(tokens, index + 2) != index + 6 || tokens[index + 7].Text != "set" || tokens[index + 8].Text != "value") return false;
        if (!SignMessageField(tokens[index + 6].Text, out var side, out var field, out var selector)) return false;
        var role = side == "front_text" ? "sign.front.line" : "sign.back.line";
        var grammarPath = At(At(path, side), field);
        var token = tokens[index + 9];
        var tokenEnd = token.Start + token.Text.Length;
        void ReadLine(int start, int end, int line)
        {
            var linePath = At(grammarPath, line);
            try { Component(start, end, linePath, storedRole: role); }
            catch (DuplicateJsonMemberException) { Unknown(linePath, start, end - start, "UnsupportedSignCommandComponent"); }
        }
        if (selector.Length != 0)
        {
            if (selector.Length != 3 || selector[1] is < '0' or > '3' || selector[2] != ']') return false;
            ReadLine(token.Start, tokenEnd, selector[1] - '0');
            return true;
        }
        var bytes = Utf8.GetBytes(token.Text);
        var parsed = Snbt.ParseDocument(bytes, MinecraftProfiles.SnbtGrammar(profile), limits);
        if (parsed.Root.Value is not LogicalMinecraftList { Items.Length: 4 }) return false;
        foreach (var child in parsed.Root.Children)
        {
            var span = child.Node.Span;
            var start = token.Start + Utf8.GetCharCount(bytes.AsSpan(0, span.StartByte));
            var end = start + Utf8.GetCharCount(bytes.AsSpan(span.StartByte, span.LengthBytes));
            ReadLine(start, end, child.Selector.Index!.Value);
        }
        return true;
    }
    private static bool SignMessageField(string path, out string side, out string field, out string selector)
    {
        side = field = selector = "";
        var fields = path.Split('.');
        if (fields.Length != 2 || fields[0] is not ("front_text" or "back_text")) return false;
        side = fields[0];
        var bracket = fields[1].IndexOf('[');
        field = bracket < 0 ? fields[1] : fields[1][..bracket];
        if (bracket >= 0) selector = fields[1][bracket..];
        return field is "messages" or "filtered_messages";
    }

    private static bool SignTextSource(string path)
    {
        var singleLine = path.StartsWith("Filtered", StringComparison.Ordinal) ? path[8..] : path;
        if (singleLine is "Text1" or "Text2" or "Text3" or "Text4") return true;
        return SignMessageField(path, out _, out _, out var selector) && (selector is "" or "[]"
            || selector.EndsWith(']') && Integer(selector[1..^1], out var line) && line is >= -4 and <= 3);
    }

    private bool StaticDataWrite(List<CommandToken> tokens, int index, SnbtNode? valueNode = null,
        bool keepEmptyListReset = false, bool resultObserved = false)
    {
        if (index + 2 >= tokens.Count || tokens[index + 1].Text != "modify") return false;
        var path = DataTargetEnd(tokens, index + 2);
        if (path < 0 || path + 3 >= tokens.Count || !StaticDataPath(tokens[path].Text, tokens[index + 2].Text == "storage")) return false;
        var value = path + 2;
        var operation = tokens[path + 1].Text;
        if (operation == "insert")
        {
            if (!Integer(tokens[value++].Text, out _)) return false;
        }
        else if (operation is not ("set" or "append" or "prepend" or "merge")) return false;
        if (tokens[value].Text == "from")
        {
            var sourcePath = DataTargetEnd(tokens, value + 1);
            if (operation == "merge" || sourcePath < 0 || tokens.Count != sourcePath + 1) return false;
            var sourceType = tokens[value + 1].Text;
            var source = tokens[sourcePath].Text;
            var nativeNumericDestination = operation == "set" && tokens[index + 2].Text == "entity" && source == tokens[path].Text
                && NumericEntitySource(source, selectorTypes.GetValueOrDefault(tokens[index + 3].Start));
            return sourceType == "entity" && (nativeNumericDestination || NumericEntitySource(source, selectorTypes.GetValueOrDefault(tokens[value + 2].Start)))
                || StaticItemSource(sourceType, source)
                || sourceType == "storage" && (!resultObserved || operation == "append" && FixedScoreStore(tokens, index)) && TaggedStorageItems()
                && InventoryTransfer(tokens[index + 2].Text, tokens[path].Text, operation, source)
                || sourceType == "block" && tokens[index + 2].Text == "storage" && (!resultObserved || operation != "set")
                && runtimeDataVersion >= StorageDataVersion && SignTextSource(source);
        }
        if (tokens.Count != value + 2 || tokens[value].Text != "value") return false;
        valueNode ??= Snbt.ParseDocument(Utf8.GetBytes(tokens[value + 1].Text), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
        if (keepEmptyListReset && operation == "set" && valueNode.Value is LogicalMinecraftList { Items.IsEmpty: true }) return false;
        return !valueNode.ContainsStringValues && (operation != "merge" || valueNode.Value is LogicalMinecraftCompound)
            || StaticItemTagPayload(tokens[path].Text, valueNode, operation)
            || operation == "set" && !resultObserved && TaggedStorageItems()
                && (AttributeMemberWrite(tokens[path].Text, valueNode) || EnchantmentIdWrite(tokens[path].Text, valueNode))
            || operation == "set" && (tokens[index + 2].Text == "entity" && tokens[path].Text == "Item" && StaticNbtMatch.ItemStack(valueNode)
                || valueNode.Value is LogicalMinecraftLeaf { Value: NbtString id } && MinecraftIdentifier.ResourceLocation(id.Value)
                && NativeItemIdPath(tokens[index + 2].Text, tokens[path].Text));
    }
    private bool TaggedStorageItems()
    {
        var items = profile.Features[ProfileFacet.ItemSchema];
        return runtimeDataVersion >= StorageDataVersion && items.Schema == "java-item-tags-1.20.4"
            && MinecraftProfiles.ForDataVersion(runtimeDataVersion)?.Features[ProfileFacet.ItemSchema] == items;
    }

    private bool InventoryTransfer(string target, string destination, string operation, string source)
    {
        if (target == "block") return destination == "Items" && operation == "set" && source.Split('.').All(PlainControlToken);
        if (target != "storage" || operation is not ("append" or "prepend" or "insert") || !PlainDataPath(destination)) return false;
        var bracket = source.IndexOf('[');
        var start = bracket + 1;
        return bracket > 0 && PlainDataPath(source[..bracket]) && source[^1] == ']' && source[start] == '{'
            && CompoundEnd(source, start) == source.Length - 1
            && StaticNbtMatch.InventoryItemFilter(Snbt.ParseDocument(Utf8.GetBytes(source[start..^1]), MinecraftProfiles.SnbtGrammar(profile), limits).Root);
    }

    private static bool AttributeMemberWrite(string path, SnbtNode payload)
        => TaggedItemMember(path, "tag.AttributeModifiers[", out var field) && StaticNbtMatch.AttributeModifierField(field, payload);

    private static bool EnchantmentIdWrite(string path, SnbtNode payload)
        => payload.Value is LogicalMinecraftLeaf { Value: NbtString id } && MinecraftIdentifier.ResourceLocation(id.Value)
            && (TaggedItemMember(path, "tag.Enchantments[", out var field) || TaggedItemMember(path, "tag.StoredEnchantments[", out field))
            && field == "id";

    private static bool TaggedItemMember(string path, string list, out string field)
    {
        field = "";
        var start = path.LastIndexOf(list, StringComparison.Ordinal);
        var end = path.LastIndexOf("].", StringComparison.Ordinal);
        if (start < 0 || start > 0 && path[start - 1] != '.' || end < start + list.Length) return false;
        var selector = start + list.Length;
        if (end != selector && !Integer(path[selector..end], out _) && !(path[selector] == '{' && CompoundEnd(path, selector) == end)) return false;
        field = path[(end + 2)..];
        return true;
    }

    private static bool NumericEntitySource(string path, string? entityType = null)
    {
        if (path is "Health" or "UUID" or "Fire" or "Air" or "FallDistance" or "PortalCooldown"
            or "Invulnerable" or "Silent" or "NoGravity" or "Glowing" or "OnGround") return true;
        if (path is "damage" or "crit" && entityType is "minecraft:arrow" or "minecraft:spectral_arrow" or "minecraft:trident") return true;
        var start = path.IndexOf('[');
        if ((start < 0 ? path : path[..start]) is not ("Pos" or "Motion" or "Rotation")) return false;
        return start < 0 || path[^1] == ']' && (start + 2 == path.Length || Integer(path[(start + 1)..^1], out _));
    }
    private bool NativeItemIdPath(string target, string path)
    {
        if (!path.EndsWith(".id", StringComparison.Ordinal)) return false;
        var item = path[..^3];
        if (target == "entity")
        {
            if (item is "Item" or "SelectedItem") return true;
            if (MinecraftProfiles.UsesCompoundEquipment(profile) && item is "equipment.head" or "equipment.chest" or "equipment.legs"
                or "equipment.feet" or "equipment.mainhand" or "equipment.offhand" or "equipment.body" or "equipment.saddle") return true;
        }
        return item.Contains('[') && StaticItemSource(target, item);
    }
    private bool StaticItemSource(string target, string path)
    {
        var bracket = path.IndexOf('[');
        var field = bracket < 0 ? path : path[..bracket];
        if (target == "entity" && path is "Item" or "SelectedItem") return true;
        if (!(target == "entity" && field is "Inventory" or "EnderItems" or "ArmorItems" or "HandItems"
            || target == "block" && field == "Items")) return false;
        if (bracket < 0) return true;
        if (path[^1] != ']') return false;
        var item = path[(bracket + 1)..^1];
        if (item.Length == 0 || Integer(item, out _)) return true;
        return item[0] == '{' && CompoundEnd(item, 0) == item.Length
            && StaticNbtMatch.ItemStack(Snbt.ParseDocument(Utf8.GetBytes(item), MinecraftProfiles.SnbtGrammar(profile), limits).Root);
    }
    private bool DataControl(List<CommandToken> tokens, int index)
    {
        if (index + 2 >= tokens.Count || tokens[index + 1].Text is not ("get" or "remove")) return false;
        var read = tokens[index + 1].Text == "get";
        var path = DataTargetEnd(tokens, index + 2);
        if (path < 0) return false;
        if (tokens.Count == path) return read;
        if (!StaticDataControlPath(tokens[index + 2].Text, tokens[path].Text)) return false;
        return tokens.Count == path + 1 || read && tokens.Count == path + 2 && Decimal(tokens[path + 1].Text, out _);
    }
    private bool StaticDataControlPath(string target, string path)
    {
        if (target == "entity" && path.StartsWith('{') && CompoundEnd(path, 0) == path.Length)
            return StaticNbtMatch.Entity(Snbt.ParseDocument(Utf8.GetBytes(path), MinecraftProfiles.SnbtGrammar(profile), limits).Root);
        return StaticDataPath(path, target == "storage") || StaticItemSource(target, path);
    }
    private static int DataTargetEnd(List<CommandToken> tokens, int target)
    {
        if (target + 1 >= tokens.Count) return -1;
        return tokens[target].Text switch
        {
            "entity" when EntityTarget(tokens[target + 1].Text) => target + 2,
            "storage" when MinecraftIdentifier.ResourceLocation(tokens[target + 1].Text) => target + 2,
            "block" when target + 3 < tokens.Count && BlockCoordinates(tokens[target + 1].Text, tokens[target + 2].Text, tokens[target + 3].Text) => target + 4,
            _ => -1
        };
    }
    private static bool PlainDataPath(string path)
    {
        var offset = 0;
        while (offset < path.Length)
        {
            if (path[offset] is '\'' or '"')
            {
                var quote = path[offset++];
                while (offset < path.Length && path[offset] != quote)
                {
                    if (path[offset++] != '\\') continue;
                    if (offset == path.Length || path[offset] != quote && path[offset] != '\\') return false;
                    offset++;
                }
                if (offset == path.Length) return false;
                offset++;
            }
            else
            {
                var start = offset;
                while (offset < path.Length && path[offset] is not ('.' or '[')) offset++;
                if (!PlainControlToken(path[start..offset])) return false;
            }
            while (offset < path.Length && path[offset] == '[')
            {
                var close = path.IndexOf(']', offset + 1);
                if (close < 0 || close > offset + 1 && !Integer(path[(offset + 1)..close], out _)) return false;
                offset = close + 1;
            }
            if (offset == path.Length) return true;
            if (path[offset++] != '.') return false;
        }
        return false;
    }
    private bool StaticDataPath(string path, bool storage)
    {
        if (PlainDataPath(path)) return true;
        var start = path.IndexOf('{');
        if (start < 0) return false;
        if (start == 0)
            return CompoundEnd(path, 0) == path.Length
                && !Snbt.ParseDocument(Utf8.GetBytes(path), MinecraftProfiles.SnbtGrammar(profile), limits).Root.ContainsStringValues;
        var structural = new StringBuilder(path.Length);
        var copied = 0;
        while (start >= 0)
        {
            if (start == 0 || path[start - 1] != '[') return false;
            var end = CompoundEnd(path, start);
            if (end >= path.Length || path[end] != ']') return false;
            var root = Snbt.ParseDocument(Utf8.GetBytes(path[start..end]), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
            if (root.Value is not LogicalMinecraftCompound || root.ContainsStringValues
                && !StaticItemTagPayload(path.AsSpan(0, start - 1), root, "filter")
                && !(storage && runtimeDataVersion >= StorageDataVersion && (StaticNbtMatch.StorageMetadataFilter(root)
                    || TaggedStorageItems() && StaticNbtMatch.InventoryItemFilter(root)))) return false;
            structural.Append(path, copied, start - copied);
            copied = end;
            start = path.IndexOf('{', copied);
        }
        return PlainDataPath(structural.Append(path, copied, path.Length - copied).ToString());
    }
    private bool StaticItemTagPayload(ReadOnlySpan<char> path, SnbtNode payload, string operation)
    {
        var itemSchema = profile.Features[ProfileFacet.ItemSchema];
        var taggedItems = itemSchema.Schema == "java-item-tags-1.20.4";
        if (operation == "set")
            return taggedItems && exactRuntimeDataVersion is int version && version >= VanillaArmorTrimDataVersion
                && ItemTagPath(path, "tag.Trim", ".tag.Trim") && MinecraftProfiles.ForDataVersion(version)?.Features[ProfileFacet.ItemSchema] == itemSchema
                && StaticNbtMatch.ArmorTrim(payload);
        if (operation is not ("append" or "prepend" or "insert" or "filter")) return false;
        if ((taggedItems || operation == "filter")
            && (ItemTagPath(path, "tag.Enchantments", ".tag.Enchantments") || ItemTagPath(path, "tag.StoredEnchantments", ".tag.StoredEnchantments")))
            return StaticNbtMatch.Enchantment(payload);
        return taggedItems && ItemTagPath(path, "tag.AttributeModifiers", ".tag.AttributeModifiers") && StaticNbtMatch.AttributeModifier(payload);
    }

    private static bool ItemTagPath(ReadOnlySpan<char> path, ReadOnlySpan<char> root, ReadOnlySpan<char> nested)
        => path.SequenceEqual(root) || path.EndsWith(nested, StringComparison.Ordinal);

    private static int CompoundEnd(string path, int start)
    {
        char quote = '\0';
        var depth = 0;
        for (var index = start; index < path.Length; index++)
        {
            var current = path[index];
            if (quote != '\0')
            {
                if (current == '\\') index++;
                else if (current == quote) quote = '\0';
            }
            else if (current is '\'' or '"') quote = current;
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return index + 1;
        }
        return path.Length;
    }
    private void ItemCommand(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
        var slot = ItemTargetEnd(tokens, index + 2);
        if (slot >= 0 && slot < tokens.Count && SingleItemSlot(tokens[slot].Text))
        {
            if (tokens[index + 1].Text == "modify" && tokens.Count == slot + 2
                && MinecraftIdentifier.ResourceLocation(tokens[slot + 1].Text)) return;
            if (tokens[index + 1].Text == "replace" && slot + 2 < tokens.Count && tokens[slot + 1].Text == "from")
            {
                var sourceSlot = ItemTargetEnd(tokens, slot + 2);
                if (sourceSlot >= 0 && sourceSlot < tokens.Count && SingleItemSlot(tokens[sourceSlot].Text)
                    && (tokens.Count == sourceSlot + 1 || tokens.Count == sourceSlot + 2 && MinecraftIdentifier.ResourceLocation(tokens[sourceSlot + 1].Text))) return;
            }
        }
        if (tokens.Count >= index + 7 && tokens[index + 1].Text == "replace" && tokens[index + 2].Text == "entity" && tokens[index + 5].Text == "with")
        { CountArgument(tokens, index + 7); ItemArgument(tokens[index + 6], At(path, "item.replace")); return; }
        if (tokens.Count >= index + 9 && tokens[index + 1].Text == "replace" && tokens[index + 2].Text == "block" && tokens[index + 7].Text == "with")
        { CountArgument(tokens, index + 9); ItemArgument(tokens[index + 8], At(path, "item.replace")); return; }
        Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownItemCommandGrammar");
    }
    private static int ItemTargetEnd(List<CommandToken> tokens, int index) => index < tokens.Count && tokens[index].Text is "entity" or "block"
        ? DataTargetEnd(tokens, index) : -1;
    private static void CountArgument(List<CommandToken> tokens, int afterItem)
    {
        if (tokens.Count > afterItem + 1 || tokens.Count == afterItem + 1 && (!int.TryParse(tokens[afterItem].Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 1))
            throw new CommandSyntaxException();
    }
}
