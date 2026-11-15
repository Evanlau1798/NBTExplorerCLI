using Next.Domain;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

internal sealed partial class CommandTextReader
{
    private void DataCommand(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
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
