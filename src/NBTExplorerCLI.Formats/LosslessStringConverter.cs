using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBTExplorerCLI.Formats;

internal sealed class LosslessStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            try { return reader.GetString(); }
            catch (InvalidOperationException error) { throw new JsonException("Use utf16be for unpaired code units", error); }
        }
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1
            || !value.TryGetProperty("utf16be", out var encoded) || encoded.ValueKind != JsonValueKind.String)
            throw new JsonException("Expected a string or exact utf16be object");
        byte[] bytes;
        try { bytes = Convert.FromHexString(encoded.GetString()!); }
        catch (FormatException error) { throw new JsonException("Invalid UTF-16 hex", error); }
        if (bytes.Length % 2 != 0) throw new JsonException("UTF-16 requires complete code units");
        var chars = new char[bytes.Length / 2];
        for (var index = 0; index < chars.Length; index++)
            chars[index] = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index * 2, 2));
        return new string(chars);
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (!HasUnpairedSurrogate(value)) { writer.WriteStringValue(value); return; }
        var bytes = new byte[checked(value.Length * 2)];
        for (var index = 0; index < value.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * 2, 2), value[index]);
        writer.WriteStartObject();
        writer.WriteString("utf16be", Convert.ToHexString(bytes));
        writer.WriteEndObject();
    }

    internal static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1])) return true;
                index++;
            }
            else if (char.IsLowSurrogate(text[index])) return true;
        }
        return false;
    }
}
