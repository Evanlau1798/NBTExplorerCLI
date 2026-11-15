using System.Text;
using Next.Domain;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

internal sealed partial class CommandTextReader
{
    private readonly Dictionary<int, string?> selectorTypes = [];
    private void Selectors(List<CommandToken> tokens, TypedPath path, Func<TypedPath, int, int, string, SourceAddress>? locate = null)
    {
        void Unresolved(TypedPath node, int start, int length, string code)
        {
            if (locate is null) { Unknown(node, start, length, code); return; }
            var address = locate(node, start, length, source.Address.SemanticRole); Charge(address, code); diagnostics.Add(new(address, code)); completeness = ReferenceCompleteness.Unresolved;
        }
        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex]; var value = token.Text;
            if (value.Length < 2 || value[0] != '@') continue;
            if (!"paresn".Contains(value[1])) { Unresolved(path, token.Start, value.Length, "UnknownSelectorGrammar"); continue; }
            if (value.Length == 2) continue;
            if (value[2] != '[' || value[^1] != ']') throw new CommandSyntaxException();
            var start = 3; var depth = 0; char quote = '\0'; var member = 0; var names = 0;
            for (var cursor = start; cursor < value.Length; cursor++)
            {
                var character = value[cursor];
                if (quote != '\0')
                {
                    if (character == '\\') { cursor++; continue; }
                    if (character == quote) quote = '\0';
                }
                else if (character is '"' or '\'') quote = character;
                else if (character is '{' or '[' or '(') depth++;
                else if (character is '}' or ']' or ')') { if (depth != 0) depth--; }
                if (quote != '\0' || depth != 0 || character != ',' && cursor != value.Length - 1) continue;
                var segmentEnd = cursor; var separator = value.IndexOf('=', start, segmentEnd - start);
                if (separator < 0) { if (segmentEnd != start) throw new CommandSyntaxException(); start = cursor + 1; continue; }
                var name = value[start..separator].Trim(); var argument = separator + 1;
                while (argument < segmentEnd && char.IsWhiteSpace(value[argument])) argument++;
                var end = segmentEnd; while (end > argument && char.IsWhiteSpace(value[end - 1])) end--;
                var node = At(At(At(path, "selector"), tokenIndex), member++);
                if (name == "name")
                {
                    var negated = argument < end && value[argument] == '!'; if (negated) argument++;
                    var decoded = SelectorString(value[argument..end]);
                    var address = locate?.Invoke(node, token.Start + argument, end - argument, "reference.selector.name") ?? Address(node, token.Start + argument, end - argument, "reference.selector.name"); Charge(address, decoded);
                    references.Add(new(address, decoded, negated));
                    if (++names > 1) completeness = ReferenceCompleteness.Unresolved;
                }
                else if (name == "type")
                {
                    var identity = value[argument..end];
                    if (!identity.StartsWith('!') && identity.Length != 0 && identity.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '.' or ':' or '/' or '-'))
                        selectorTypes[token.Start] = selectorTypes.ContainsKey(token.Start) ? null : MinecraftIdentifier.DefaultNamespace(identity);
                }
                else if (name == "nbt") Unresolved(node, token.Start + argument, end - argument, "DynamicNbtSelectorReference");
                else if (name is not ("type" or "tag" or "team" or "scores" or "advancements" or "predicate" or "limit" or "sort" or "x" or "y" or "z" or "dx" or "dy" or "dz" or "distance" or "level" or "gamemode" or "x_rotation" or "y_rotation"))
                    Unresolved(node, token.Start + argument, end - argument, "UnknownSelectorOption");
                start = cursor + 1;
            }
        }
    }
    private static string SelectorString(string spelling)
    {
        if (spelling.Length == 0) return "";
        if (spelling[0] is not ('"' or '\''))
        {
            if (spelling.Any(character => char.IsWhiteSpace(character) || character is '"' or '\'' or '\\')) throw new CommandSyntaxException();
            return spelling;
        }
        var quote = spelling[0];
        if (spelling.Length < 2 || spelling[^1] != quote) throw new CommandSyntaxException();
        var result = new StringBuilder();
        for (var index = 1; index < spelling.Length - 1; index++)
        {
            var character = spelling[index];
            if (character == '\\')
            {
                if (++index >= spelling.Length - 1 || spelling[index] != quote && spelling[index] != '\\') throw new CommandSyntaxException();
                character = spelling[index];
            }
            else if (character == quote) throw new CommandSyntaxException();
            result.Append(character);
        }
        return result.ToString();
    }
}
