using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class CommandDraftOperand
{
    public static SnbtSpan? Find(string line, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            var tokens = CommandTokens.Read(line, 0, limits ?? DecodeLimits.Default);
            if (tokens.Count < 2 || tokens[0].Text.StartsWith('#') || tokens[^1].Text[0] is not ('"' or '\'' or '{' or '['))
                return null;
            var last = tokens[^1];
            return new(Encoding.UTF8.GetByteCount(line.AsSpan(0, last.Start)), Encoding.UTF8.GetByteCount(last.Text));
        }
        catch (CommandSyntaxException) { return null; }
    }
}
