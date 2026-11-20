using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class ClickCommandText
{
    public const string Role = "command.click.message";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal sealed record Binding(string? Text = null, string? Prefix = null, byte[]? Component = null, TypedPath? Path = null);
    internal static readonly Binding NoText = new();

    internal static Binding? Read(string command, SnbtProfile? grammar, DecodeLimits limits)
    {
        if (!command.StartsWith('/') || command.Any(ch => ch is '\r' or '\n' or '\0')) return null;
        if (command.StartsWith("/kick @s ", StringComparison.Ordinal))
            return command.Length > 9 && !command.AsSpan(9).Contains('@') ? new(command[9..], command[..9]) : null;
        try
        {
            var tokens = CommandTokens.Read(command, 0, limits);
            if (tokens.Count == 2 && tokens[0].Text == "/function" && CommandTextReader.ResourceOrTag(tokens[1].Text)) return NoText;
            if (tokens.Count == 7 && tokens[0].Text == "/execute" && tokens[1].Text is "if" or "unless" && tokens[2].Text == "entity"
                && tokens[4].Text == "run" && tokens[5].Text == "function" && CommandTextReader.ResourceOrTag(tokens[6].Text)
                && SelectorNames.Parse(tokens[3].Text, limits, allowNearest: false) is { Complete: true, Names.IsEmpty: true }) return NoText;
            if (tokens.Count == 5 && tokens[0].Text is "/tp" or "/teleport" && tokens[1].Text is "@s" or "@p" or "@a"
                && tokens.Skip(2).All(token => CommandTextReader.Coordinate(token.Text))) return NoText;
            if (tokens.Count != 3 || tokens[0].Text != "/tellraw" || tokens[1].Text is not ("@s" or "@p" or "@a")) return null;
            var start = tokens[2].Start;
            var bytes = Utf8.GetBytes(command[start..]);
            var path = TypedPath.Root;
            if (grammar is { } snbt)
            {
                var parsed = Snbt.ParseDocument(bytes, snbt, limits);
                var value = parsed.Root.Value;
                if (value is LogicalMinecraftList { Items.Length: 1 } list) { path = Child(path, PathSegment.ForIndex(0)); value = list.Items[0]; }
                if (value is not LogicalMinecraftCompound compound || compound.Entries.Any(entry => !LiteralField(entry.Name))
                    || compound.Entries.SingleOrDefault(entry => entry.Name == "text")?.Value is not LogicalMinecraftLeaf { Value: NbtString text }) return null;
                return new(text.Value, command[..start], bytes, Child(path, PathSegment.ForKey("text")));
            }
            using var json = BoundedJson.ReadDecoded(bytes, limits);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1) { path = Child(path, PathSegment.ForIndex(0)); root = root[0]; }
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(item => !LiteralField(NativeJsonStrings.PropertyName(item)))
                || !root.TryGetNativeProperty("text", out var literal) || literal.ValueKind != JsonValueKind.String) return null;
            return new(NativeJsonStrings.Read(literal), command[..start], bytes, Child(path, PathSegment.ForKey("text")));
        }
        catch (CommandSyntaxException) { return null; }
        catch (JsonException) { return null; }
        catch (SnbtException error) when (error.Code != SnbtError.LimitExceeded) { return null; }
    }

    public static string Rewrite(string command, string expected, string replacement, SnbtProfile? grammar, DecodeLimits limits)
    {
        var binding = Read(command, grammar, limits);
        if (binding?.Text is null || binding.Text != expected) throw new EditRejectedException(EditFailure.Precondition, "Click command message preimage differs");
        if (expected == replacement) return command;
        if (replacement.Any(ch => ch is '\r' or '\n' or '\0')) throw new EditRejectedException(EditFailure.Precondition, "Click commands require single-line messages");
        string output;
        if (binding.Component is not { } bytes) output = binding.Prefix + replacement;
        else if (grammar is { } snbt)
        {
            var parsed = Snbt.ParseDocument(bytes, snbt, limits);
            var span = parsed.Resolve(binding.Path!).Span;
            var literal = Snbt.Write(new LogicalMinecraftLeaf(new NbtString(replacement)), snbt, limits);
            output = binding.Prefix + Utf8.GetString(bytes.AsSpan(0, span.StartByte)) + Utf8.GetString(literal) + Utf8.GetString(bytes.AsSpan(span.StartByte + span.LengthBytes));
        }
        else output = binding.Prefix + Utf8.GetString(NativeJsonEdits.ReplaceStrings(bytes, [(binding.Path!, expected, replacement)], limits));
        if (Read(output, grammar, limits)?.Text != replacement) throw new EditRejectedException(EditFailure.Precondition, "Click command candidate changes its supported grammar");
        return output;
    }

    private static bool LiteralField(string key) => key is "text" or "color" or "bold" or "italic" or "underlined" or "strikethrough" or "obfuscated" or "font";
    private static TypedPath Child(TypedPath path, PathSegment segment) => new(path.Segments.Add(segment));
}
