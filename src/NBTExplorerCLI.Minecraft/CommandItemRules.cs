using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void ItemArgument(CommandToken token, TypedPath path)
    {
        var value = token.Text; var open = value.IndexOf('[');
        if (profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4")
        {
            var tag = value.IndexOf('{');
            if (tag < 0) return;
            var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, "item.tag", source.Address.Dimension, source.Address.Chunk);
            var extracted = SnbtTextExtractor.ItemNbtTag(binding, value[tag..], profile, MinecraftIdentifier.DefaultNamespace(value[..tag]), limits, scope, StoredComponentCallback(path, token.Start + tag, token.Start + value.Length), commandTag: true);
            MergeComponent(extracted, path, token.Start + tag, token.Start + value.Length, true);
            if (extracted.Diagnostics.IsEmpty) ComponentReferences(Utf8.GetBytes(value[tag..]), token.Start + tag, token.Start + value.Length, path, true, itemTree: true, commandTag: true);
            return;
        }
        if (open < 0) return;
        if (value[^1] != ']' || profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4") throw new CommandSyntaxException();
        var cursor = open + 1; var seen = new HashSet<string>(StringComparer.Ordinal);
        while (cursor < value.Length - 1)
        {
            while (cursor < value.Length - 1 && char.IsWhiteSpace(value[cursor])) cursor++;
            var keyStart = cursor; while (cursor < value.Length - 1 && value[cursor] is not ('=' or ',')) cursor++;
            if (cursor >= value.Length - 1 || value[cursor] != '=') throw new CommandSyntaxException();
            var key = value[keyStart..cursor].Trim(); var name = MinecraftIdentifier.DefaultNamespace(key); var node = At(path, key);
            if (!seen.Add(name)) { Unknown(node, token.Start + keyStart, cursor - keyStart, "AmbiguousItemComponent"); return; }
            var start = ++cursor;
            var bytes = Utf8.GetBytes(value[start..^1]);
            var parsed = Snbt.ParseFragment(bytes, 0, MinecraftProfiles.SnbtGrammar(profile), limits);
            var characters = Utf8.GetCharCount(bytes.AsSpan(0, parsed.ConsumedSpan.EndByte)); cursor += characters;
            if (name is "minecraft:custom_name" or "minecraft:item_name" or "minecraft:lore" or "minecraft:written_book_content" or "minecraft:writable_book_content"
                or "minecraft:container" or "minecraft:bundle_contents" or "minecraft:charged_projectiles" or "minecraft:block_entity_data" or "minecraft:entity_data")
                Component(token.Start + start, token.Start + cursor, node, name);
            if (cursor < value.Length - 1)
            {
                if (value[cursor++] != ',') throw new CommandSyntaxException();
                if (cursor == value.Length - 1) throw new CommandSyntaxException();
            }
        }
    }
}
