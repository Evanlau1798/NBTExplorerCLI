using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void DataCommand(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
        var storageElement = storageAccess is not null || storageDisplayLists is { Count: > 0 } ? StorageDisplayWrite(tokens, index) : null;
        var staticListWrite = storageElement is not null && StaticDataWrite(tokens, index);
        if (storageAccess is not null) ObserveDataStorage(tokens, index, path, staticListWrite ? null : storageElement);
        if (staticListWrite || ReadStorageDisplayWrite(tokens, path, storageElement)) return;
        if (DataControl(tokens, index)) return;
        if (SignMessageWrite(tokens, index, path)) return;
        if (tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "entity"
            && EntityTarget(tokens[index + 3].Text) && tokens[index + 4].Text == "CustomName" && tokens[index + 5].Text == "set" && tokens[index + 6].Text == "value")
        {
            var target = tokens[index + 3]; var value = tokens[index + 7];
            var role = selectorTypes.GetValueOrDefault(target.Start) == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name";
            Component(value.Start, value.Start + value.Text.Length, At(path, "data.entity.CustomName"), storedRole: role);
            return;
        }
        var blockItems = tokens.Count == index + 10 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "block"
            && BlockCoordinates(tokens[index + 3].Text, tokens[index + 4].Text, tokens[index + 5].Text)
            && tokens[index + 6].Text == "Items" && tokens[index + 7].Text is "append" or "prepend" && tokens[index + 8].Text == "value";
        var droppedItem = tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "entity"
            && EntityTarget(tokens[index + 3].Text) && selectorTypes.GetValueOrDefault(tokens[index + 3].Start) == "minecraft:item"
            && tokens[index + 4].Text == "Item" && tokens[index + 5].Text == "set" && tokens[index + 6].Text == "value";
        if (blockItems || droppedItem)
        {
            var token = tokens[^1]; var grammarPath = At(path, blockItems ? "data.block.Items" : "data.entity.Item");
            var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, "item.stack", source.Address.Dimension, source.Address.Chunk);
            var tokenEnd = token.Start + token.Text.Length;
            var result = SnbtTextExtractor.ItemStack(binding, token.Text, profile, limits, scope, StoredComponentCallback(grammarPath, token.Start, tokenEnd));
            MergeComponent(result, grammarPath, token.Start, tokenEnd, true);
            if (result.Diagnostics.IsEmpty) ComponentReferences(Utf8.GetBytes(token.Text), token.Start, tokenEnd, grammarPath, true, itemTree: true, commandTag: true);
            return;
        }
        if (StaticDataWrite(tokens, index)) return;
        if (tokens.Count >= index + 5 && tokens[index + 1].Text == "merge" && tokens[index + 2].Text == "entity")
        {
            if (tokens.Count != index + 5) throw new CommandSyntaxException();
            if (selectorTypes.GetValueOrDefault(tokens[index + 3].Start) is { } identity)
            {
                Structured(tokens[index + 4].Start, tokens[index + 4].Start + tokens[index + 4].Text.Length, At(path, "data.entity.nbt"), identity, false); return;
            }
        }
        Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownDataTextSchema");
    }
    private bool SignMessageWrite(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (tokens.Count != index + 10 || tokens[index + 1].Text != "modify" || tokens[index + 2].Text != "block"
            || DataTargetEnd(tokens, index + 2) != index + 6 || tokens[index + 7].Text != "set" || tokens[index + 8].Text != "value") return false;
        var fields = tokens[index + 6].Text.Split('.');
        if (fields.Length != 2 || fields[0] is not ("front_text" or "back_text")) return false;
        var bracket = fields[1].IndexOf('[');
        var field = bracket < 0 ? fields[1] : fields[1][..bracket];
        if (field is not ("messages" or "filtered_messages")) return false;
        var role = fields[0] == "front_text" ? "sign.front.line" : "sign.back.line";
        var grammarPath = At(At(path, fields[0]), field);
        var token = tokens[index + 9];
        var tokenEnd = token.Start + token.Text.Length;
        void ReadLine(int start, int end, int line)
        {
            var linePath = At(grammarPath, line);
            try { Component(start, end, linePath, storedRole: role); }
            catch (DuplicateJsonMemberException) { Unknown(linePath, start, end - start, "UnsupportedSignCommandComponent"); }
        }
        if (bracket >= 0)
        {
            var suffix = fields[1].AsSpan(bracket);
            if (suffix.Length != 3 || suffix[1] is < '0' or > '3' || suffix[2] != ']') return false;
            ReadLine(token.Start, tokenEnd, suffix[1] - '0');
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
    private bool StaticDataWrite(List<CommandToken> tokens, int index)
    {
        if (index + 2 >= tokens.Count || tokens[index + 1].Text != "modify") return false;
        var path = DataTargetEnd(tokens, index + 2);
        if (path < 0 || path + 3 >= tokens.Count || !StaticDataPath(tokens[path].Text)) return false;
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
            return sourceType == "entity" && NumericEntitySource(source) || StaticItemSource(sourceType, source);
        }
        if (tokens.Count != value + 2 || tokens[value].Text != "value") return false;
        var parsed = Snbt.ParseDocument(Utf8.GetBytes(tokens[value + 1].Text), MinecraftProfiles.SnbtGrammar(profile), limits);
        return !parsed.Root.ContainsStringValues && (operation != "merge" || parsed.Root.Value is LogicalMinecraftCompound)
            || StaticItemTagPayload(tokens[path].Text, parsed.Root, operation);
    }
    private static bool NumericEntitySource(string path)
    {
        if (path is "Health" or "UUID") return true;
        var start = path.IndexOf('[');
        if ((start < 0 ? path : path[..start]) is not ("Pos" or "Motion" or "Rotation")) return false;
        return start < 0 || path[^1] == ']' && (start + 2 == path.Length || Integer(path[(start + 1)..^1], out _));
    }
    private bool StaticItemSource(string target, string path)
    {
        var bracket = path.IndexOf('[');
        var field = bracket < 0 ? path : path[..bracket];
        if (target == "entity" && path == "SelectedItem") return true;
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
        return StaticDataPath(path) || StaticItemSource(target, path);
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
        foreach (var segment in path.Split('.'))
        {
            var bracket = segment.IndexOf('[');
            if (!PlainControlToken(bracket < 0 ? segment : segment[..bracket])) return false;
            while (bracket >= 0 && bracket < segment.Length)
            {
                if (segment[bracket] != '[') return false;
                var close = segment.IndexOf(']', bracket + 1);
                if (close < 0 || close > bracket + 1 && !Integer(segment[(bracket + 1)..close], out _)) return false;
                bracket = close + 1;
            }
        }
        return true;
    }
    private bool StaticDataPath(string path)
    {
        var start = path.IndexOf('{');
        if (start < 0) return PlainDataPath(path);
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
            if (root.Value is not LogicalMinecraftCompound || root.ContainsStringValues && !StaticItemTagPayload(path.AsSpan(0, start - 1), root, "filter")) return false;
            structural.Append(path, copied, start - copied);
            copied = end;
            start = path.IndexOf('{', copied);
        }
        return PlainDataPath(structural.Append(path, copied, path.Length - copied).ToString());
    }
    private bool StaticItemTagPayload(ReadOnlySpan<char> path, SnbtNode payload, string operation)
    {
        if (operation is not ("append" or "prepend" or "insert" or "filter")) return false;
        var taggedItems = profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4";
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
