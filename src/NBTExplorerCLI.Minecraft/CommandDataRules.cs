using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void DataCommand(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
        if (tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "entity"
            && EntityTarget(tokens[index + 3].Text) && tokens[index + 4].Text == "CustomName" && tokens[index + 5].Text == "set" && tokens[index + 6].Text == "value")
        {
            var target = tokens[index + 3]; var value = tokens[index + 7];
            var role = selectorTypes.GetValueOrDefault(target.Start) == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name";
            Component(value.Start, value.Start + value.Text.Length, At(path, "data.entity.CustomName"), storedRole: role);
            return;
        }
        if (tokens.Count == index + 10 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "block"
            && BlockCoordinates(tokens[index + 3].Text, tokens[index + 4].Text, tokens[index + 5].Text)
            && tokens[index + 6].Text == "Items" && tokens[index + 7].Text is "append" or "prepend" && tokens[index + 8].Text == "value")
        {
            var token = tokens[index + 9]; var grammarPath = At(path, "data.block.Items");
            var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, "item.stack", source.Address.Dimension, source.Address.Chunk);
            var result = SnbtTextExtractor.ItemStack(binding, token.Text, profile, limits, scope, ComponentCallback(grammarPath, token.Start, true));
            var tokenEnd = token.Start + token.Text.Length;
            MergeComponent(result, grammarPath, token.Start, tokenEnd, true);
            if (result.Diagnostics.IsEmpty) ComponentReferences(Utf8.GetBytes(token.Text), token.Start, tokenEnd, grammarPath, true, itemTree: true, commandTag: true);
            return;
        }
        if (profile.Id == "java-1.20.1" && tokens.Count == index + 8
            && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "entity" && EntityTarget(tokens[index + 3].Text)
            && tokens[index + 4].Text is ("CustomNameVisible" or "Invulnerable" or "Glowing")
            && tokens[index + 5].Text == "set" && tokens[index + 6].Text == "value"
            && tokens[index + 7].Text is ("0b" or "1b" or "0B" or "1B"))
            return;
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
    private void ItemCommand(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
        if (tokens.Count >= index + 7 && tokens[index + 1].Text == "replace" && tokens[index + 2].Text == "entity" && tokens[index + 5].Text == "with")
        { CountArgument(tokens, index + 7); ItemArgument(tokens[index + 6], At(path, "item.replace")); return; }
        if (tokens.Count >= index + 9 && tokens[index + 1].Text == "replace" && tokens[index + 2].Text == "block" && tokens[index + 7].Text == "with")
        { CountArgument(tokens, index + 9); ItemArgument(tokens[index + 8], At(path, "item.replace")); return; }
        Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownItemCommandGrammar");
    }
    private static void CountArgument(List<CommandToken> tokens, int afterItem)
    {
        if (tokens.Count > afterItem + 1 || tokens.Count == afterItem + 1 && (!int.TryParse(tokens[afterItem].Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 1))
            throw new CommandSyntaxException();
    }
}
