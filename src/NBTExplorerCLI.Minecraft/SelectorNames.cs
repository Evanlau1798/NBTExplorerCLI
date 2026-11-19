using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;

namespace NBTExplorerCLI.Minecraft;

public sealed record SelectorNameToken(int Member, int Start, int Length, string Name, bool Negated);
public sealed record SelectorNameDiagnostic(int Member, int Start, int Length, string Code);
public sealed record SelectorNameResult(char Kind, string? EntityType, ImmutableArray<SelectorNameToken> Names, ImmutableArray<SelectorNameDiagnostic> Diagnostics)
{
    public bool Complete => Diagnostics.IsEmpty;
}

public static class SelectorNames
{
    public static SelectorNameResult Parse(string selector, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        limits ??= DecodeLimits.Default; limits.Validate();
        if ((long)selector.Length * 2 > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Selector exceeds retention budget");
        var names = ImmutableArray.CreateBuilder<SelectorNameToken>(); var diagnostics = ImmutableArray.CreateBuilder<SelectorNameDiagnostic>();
        var kind = selector.Length >= 2 && selector[0] == '@' ? selector[1] : '\0';
        string? type = null; var hasType = false; long retained = 0;
        SelectorNameResult Finish() => new(kind, type, names.ToImmutable(), diagnostics.ToImmutable());
        void Charge(int characters)
        {
            retained = checked(retained + 256L + (long)characters * 2);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Selector results exceed retention budget");
        }
        void Unknown(int member, int start, int length, string code) { Charge(code.Length); diagnostics.Add(new(member, start, length, code)); }
        try
        {
            var tokens = CommandTokens.Read(selector, 0, limits);
            if (tokens.Count != 1 || tokens[0].Text != selector) throw new CommandSyntaxException();
            if (!"paresn".Contains(kind)) { Unknown(-1, 0, selector.Length, "UnknownSelectorGrammar"); return Finish(); }
            if (selector.Length == 2) return Finish();
            if (selector[2] != '[' || selector[^1] != ']') throw new CommandSyntaxException();
            var start = 3; var depth = 0; char quote = '\0'; var member = 0;
            for (var cursor = start; cursor < selector.Length; cursor++)
            {
                var character = selector[cursor];
                if (quote != '\0')
                {
                    if (character == '\\') { cursor++; continue; }
                    if (character == quote) quote = '\0';
                }
                else if (character is '"' or '\'') quote = character;
                else if (character is '{' or '[' or '(') depth++;
                else if (character is '}' or ']' or ')') { if (depth != 0) depth--; }
                if (quote != '\0' || depth != 0 || character != ',' && cursor != selector.Length - 1) continue;
                var end = cursor; var separator = selector.IndexOf('=', start, end - start);
                if (separator < 0)
                {
                    if (end != start || cursor != selector.Length - 1) throw new CommandSyntaxException();
                    start = cursor + 1; continue;
                }
                if (member >= limits.MaxNodes) throw new CodecException(CodecError.LimitExceeded, cursor, "Selector member budget exceeded");
                var option = selector[start..separator].Trim(); var argument = separator + 1;
                while (argument < end && char.IsWhiteSpace(selector[argument])) argument++;
                while (end > argument && char.IsWhiteSpace(selector[end - 1])) end--;
                if (option == "name")
                {
                    var negated = argument < end && selector[argument] == '!'; if (negated) argument++;
                    var decoded = Decode(selector[argument..end]); Charge(decoded.Length);
                    names.Add(new(member, argument, end - argument, decoded, negated));
                    if (names.Count > 1) Unknown(member, argument, end - argument, "UnresolvedSelectorNames");
                }
                else if (option == "type")
                {
                    var identity = selector[argument..end];
                    if (!identity.StartsWith('!') && identity.Length != 0 && identity.All(unit => char.IsAsciiLetterLower(unit) || char.IsAsciiDigit(unit) || unit is '_' or '.' or ':' or '/' or '-'))
                    { type = hasType ? null : MinecraftIdentifier.DefaultNamespace(identity); hasType = true; }
                }
                else if (option == "nbt")
                {
                    if (!NameFreeNbt(selector.AsSpan(argument, end - argument), limits))
                        Unknown(member, argument, end - argument, "DynamicNbtSelectorReference");
                }
                else if (option is not ("tag" or "team" or "scores" or "advancements" or "predicate" or "limit" or "sort" or "x" or "y" or "z" or "dx" or "dy" or "dz" or "distance" or "level" or "gamemode" or "x_rotation" or "y_rotation"))
                    Unknown(member, argument, end - argument, "UnknownSelectorOption");
                start = cursor + 1; member++;
            }
        }
        catch (CommandSyntaxException) { Unknown(-1, 0, selector.Length, "InvalidCommandSyntax"); }
        return Finish();
    }
    private static bool NameFreeNbt(ReadOnlySpan<char> spelling, DecodeLimits limits)
    {
        if (spelling.StartsWith("!")) spelling = spelling[1..].TrimStart();
        try
        {
            var root = Snbt.ParseDocument(Encoding.UTF8.GetBytes(spelling.ToString()), limits: limits).Root;
            return root.Value is LogicalMinecraftCompound && NoStringLeaves(root);
        }
        catch (SnbtException error) when (error.Code != SnbtError.LimitExceeded) { return false; }
    }
    private static bool NoStringLeaves(SnbtNode node)
        => node.Value is not LogicalMinecraftLeaf { Value: NbtString } && node.Children.All(child => NoStringLeaves(child.Node));

    private static string Decode(string spelling)
    {
        if (spelling.Length == 0) return "";
        if (spelling[0] is not ('"' or '\''))
        {
            if (spelling.Any(unit => char.IsWhiteSpace(unit) || unit is '"' or '\'' or '\\')) throw new CommandSyntaxException();
            return spelling;
        }
        var quote = spelling[0];
        if (spelling.Length < 2 || spelling[^1] != quote) throw new CommandSyntaxException();
        var result = new StringBuilder();
        for (var index = 1; index < spelling.Length - 1; index++)
        {
            var unit = spelling[index];
            if (unit == '\\')
            {
                if (++index >= spelling.Length - 1 || spelling[index] != quote && spelling[index] != '\\') throw new CommandSyntaxException();
                unit = spelling[index];
            }
            else if (unit == quote) throw new CommandSyntaxException();
            result.Append(unit);
        }
        return result.ToString();
    }
}
