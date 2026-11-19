using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class ClassicTextJson
{
    public static string Normalize(string input, DecodeLimits limits, bool storedComponent = false)
    {
        _ = NativeJsonStrings.EncodeJavaDocument(input, limits);
        var trimmed = input.AsSpan().Trim();
        if (trimmed.IsEmpty || trimmed.SequenceEqual("null")) return "null";
        if (storedComponent && trimmed[0] is not ('{' or '[' or '"' or '\''))
            return JsonSerializer.Serialize(input);

        var output = new StringBuilder(input.Length);
        for (var cursor = 0; cursor < input.Length;)
        {
            var character = input[cursor];
            if (character is '"' or '\'')
            {
                var quote = character; var start = cursor++;
                while (cursor < input.Length && input[cursor] != quote)
                    cursor += input[cursor] == '\\' ? 2 : 1;
                if (cursor >= input.Length) throw new JsonException("Unterminated 1.8.1 component string");
                if (quote == '"') output.Append(input, start, ++cursor - start);
                else
                {
                    output.Append('"');
                    for (var index = start + 1; index < cursor; index++)
                    {
                        var current = input[index];
                        if (current == '\\')
                        {
                            var escaped = input[++index];
                            if (escaped != '\'') output.Append('\\');
                            output.Append(escaped);
                        }
                        else { if (current == '"') output.Append('\\'); output.Append(current); }
                    }
                    output.Append('"'); cursor++;
                }
            }
            else if (Bare(character))
            {
                var start = cursor++;
                while (cursor < input.Length && Bare(input[cursor])) cursor++;
                var token = input[start..cursor]; var next = cursor;
                while (next < input.Length && char.IsWhiteSpace(input[next])) next++;
                var key = next < input.Length && input[next] == ':';
                if (!key && token is "null" or "true" or "false") output.Append(token);
                else if (!key && IsNumber(token)) output.Append(token);
                else output.Append(JsonSerializer.Serialize(token));
            }
            else { output.Append(character); cursor++; }
            if (output.Length > limits.MaxDecodedBytes)
                throw new CodecException(CodecError.LimitExceeded, cursor, "Normalized 1.8.1 component exceeds byte budget");
        }
        var normalized = output.ToString();
        _ = NativeJsonStrings.EncodeJavaDocument(normalized, limits);
        return normalized;
    }

    private static bool Bare(char value) => !char.IsWhiteSpace(value) && value is not ('{' or '}' or '[' or ']' or ':' or ',' or '"' or '\'' or '/' or '\\' or '#' or ';' or '=');
    private static bool IsNumber(string value)
    {
        if (value[0] is not '-' && !char.IsAsciiDigit(value[0])) return false;
        try { using var json = JsonDocument.Parse(value); return json.RootElement.ValueKind == JsonValueKind.Number; }
        catch (JsonException) { return false; }
    }
}
