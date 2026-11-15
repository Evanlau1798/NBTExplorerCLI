using Next.Domain;

namespace Next.Minecraft;

internal sealed class CommandSyntaxException : Exception;
internal sealed record CommandToken(string Text, int Start);
internal static class CommandTokens
{
    internal static List<CommandToken> Read(string line, int offset, DecodeLimits limits)
    {
        var result = new List<CommandToken>();
        for (var index = 0; index < line.Length;)
        {
            if (line[index] is ' ' or '\t') { index++; continue; }
            var start = index; char quote = '\0'; var brackets = new Stack<char>();
            while (index < line.Length)
            {
                var character = line[index];
                if (quote != '\0')
                {
                    if (character == '\\') { index += 2; if (index > line.Length) throw new CommandSyntaxException(); continue; }
                    if (character == quote) quote = '\0';
                }
                else if (character is '\'' or '"') quote = character;
                else if (character is '{' or '[' or '(') { brackets.Push(character); if (brackets.Count > limits.MaxDepth) throw new CodecException(CodecError.LimitExceeded, index, "Command depth budget exceeded"); }
                else if (character is '}' or ']' or ')')
                {
                    if (brackets.Count == 0 || (brackets.Pop(), character) is not (('{', '}') or ('[', ']') or ('(', ')'))) throw new CommandSyntaxException();
                }
                else if (brackets.Count == 0 && character is ' ' or '\t') break;
                index++;
            }
            if (quote != '\0' || brackets.Count != 0) throw new CommandSyntaxException();
            if (result.Count >= limits.MaxNodes) throw new CodecException(CodecError.LimitExceeded, index, "Command token budget exceeded");
            result.Add(new(line[start..index], start + offset));
        }
        return result;
    }
}
