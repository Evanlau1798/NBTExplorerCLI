using System.Text.Json;
using System.Text;
using System.Globalization;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class NativeJsonStrings
{
    public static bool TryGetNativeProperty(this JsonElement value, string name, out JsonElement property)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (value.ValueKind != JsonValueKind.Object) { property = default; return false; }
        try { return value.TryGetProperty(name, out property); }
        catch (InvalidOperationException)
        {
            var found = false; property = default;
            foreach (var item in value.EnumerateObject())
                if (PropertyName(item) == name) { property = item.Value; found = true; }
            return found;
        }
    }
    public static byte[] EncodeJavaDocument(string source, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); limits ??= DecodeLimits.Default; limits.Validate();
        StringBuilder? escapedSource = null;
        var quoted = false; var escape = false; long bytes = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            var paired = char.IsHighSurrogate(character) && index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]);
            var unpaired = char.IsSurrogate(character) && !paired;
            bytes += paired ? 4 : unpaired ? 6 : character < 0x80 ? 1 : character < 0x800 ? 2 : 3;
            if (bytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, index, "Encoded Java JSON input exceeds byte budget");
            if (unpaired)
            {
                if (!quoted || escape) throw new JsonException("Unpaired code unit outside a JSON string or in an invalid escape");
                if (escapedSource is null) { escapedSource = new(Math.Min(source.Length, 4096)); escapedSource.Append(source.AsSpan(0, index)); }
                escapedSource.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                escapedSource?.Append(character);
                if (paired) { index++; escapedSource?.Append(source[index]); escape = false; continue; }
            }
            if (escape) escape = false;
            else if (quoted && character == '\\') escape = true;
            else if (character == '"') quoted = !quoted;
        }
        return new UTF8Encoding(false, true).GetBytes(escapedSource?.ToString() ?? source);
    }
    public static string Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw new JsonException("Expected a native JSON string");
        try { return value.GetString()!; }
        catch (InvalidOperationException) { return FirstString(value.GetRawText()); }
    }
    public static string PropertyName(JsonProperty property)
    {
        try { return property.Name; }
        catch (InvalidOperationException) { return FirstString(property.ToString()); }
    }
    private static string FirstString(string spelling)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(spelling));
        if (!reader.Read()) throw new JsonException("Missing JSON string");
        return ReadToken(ref reader);
    }
    public static string ReadToken(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) throw new JsonException("Expected a JSON string token");
        try { return reader.GetString()!; }
        catch (InvalidOperationException)
        {
            // Gson preserves escaped Java UTF16 code units; BCL GetString rejects unpaired surrogates.
            var raw = new UTF8Encoding(false, true).GetString(reader.ValueSpan);
            var decoded = new StringBuilder(raw.Length);
            for (var index = 0; index < raw.Length; index++)
            {
                var character = raw[index];
                if (character != '\\') { decoded.Append(character); continue; }
                character = raw[++index];
                if (character == 'u')
                {
                    decoded.Append((char)ushort.Parse(raw.AsSpan(index + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); index += 4;
                }
                else decoded.Append(character switch
                {
                    '"' or '\\' or '/' => character, 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                    _ => throw new JsonException("Invalid JSON escape")
                });
            }
            return decoded.ToString();
        }
    }
}
