using Next.Domain;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Next.Formats;

internal sealed class NbtContractJsonConverter : JsonConverter<NbtValue>
{
    public override NbtValue Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var nodes = 0;
        return ReadValue(document.RootElement, options, 0, ref nodes);
    }
    private static NbtValue ReadValue(JsonElement node, JsonSerializerOptions options, int depth, ref int nodes)
    {
        Budget(depth, ref nodes);
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("tag", out var tag)) throw new JsonException("Missing typed NBT tag");
        var name = tag.GetString();
        ContractJson.Fields(node, name == "list" ? ["tag", "elementType", "value"] : ["tag", "value"]);
        var value = node.GetProperty("value");
        return name switch
        {
            "byte" => new NbtByte(value.GetSByte()),
            "short" => new NbtShort(value.GetInt16()),
            "int" => new NbtInt(value.GetInt32()),
            "long" => new NbtLong(ParseLong(value)),
            "float" => new NbtFloat(unchecked((int)ParseBits(value, 8))),
            "double" => new NbtDouble(unchecked((long)ParseBits(value, 16))),
            "string" => new NbtString(value.Deserialize<string>(options) ?? throw new JsonException("Missing NBT string")),
            "byteArray" => new NbtByteArray(value.EnumerateArray().Select(item => item.GetSByte())),
            "intArray" => new NbtIntArray(value.EnumerateArray().Select(item => item.GetInt32())),
            "longArray" => new NbtLongArray(value.EnumerateArray().Select(ParseLong)),
            "list" => ReadList(node, options, depth, ref nodes),
            "compound" => ReadCompound(value, options, depth, ref nodes),
            _ => throw new JsonException("Unknown typed NBT tag")
        };
    }
    private static NbtList ReadList(JsonElement node, JsonSerializerOptions options, int depth, ref int nodes)
    {
        var name = node.GetProperty("elementType").GetString();
        if (!Enum.TryParse<NbtType>(name, true, out var type) || !Enum.IsDefined(type) || Name(type) != name)
            throw new JsonException("Invalid list element type");
        var values = new List<NbtValue>();
        foreach (var item in node.GetProperty("value").EnumerateArray()) values.Add(ReadValue(item, options, depth + 1, ref nodes));
        return new(type, values);
    }
    private static NbtCompound ReadCompound(JsonElement array, JsonSerializerOptions options, int depth, ref int nodes)
    {
        var entries = new List<NbtEntry>();
        foreach (var entry in array.EnumerateArray())
        {
            ContractJson.Fields(entry, "name", "value");
            var name = entry.GetProperty("name").Deserialize<string>(options) ?? throw new JsonException("Missing NBT name");
            entries.Add(new(name, ReadValue(entry.GetProperty("value"), options, depth + 1, ref nodes)));
        }
        return new(entries);
    }
    private static long ParseLong(JsonElement element)
        => long.Parse(element.GetString() ?? throw new JsonException("Long must be a decimal string"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static ulong ParseBits(JsonElement element, int digits)
    {
        var text = element.GetString();
        if (text is null || text.Length != digits || !text.All(Uri.IsHexDigit)) throw new JsonException("Expected exact raw-bit hex");
        return ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }
    private static string Name(NbtType type) => type switch
    {
        NbtType.ByteArray => "byteArray", NbtType.IntArray => "intArray", NbtType.LongArray => "longArray",
        _ => type.ToString().ToLowerInvariant()
    };
    private static void Budget(int depth, ref int nodes)
    {
        if (depth > 256 || ++nodes > 100_000) throw new JsonException("Typed NBT contract exceeds depth/node budget");
    }
    public override void Write(Utf8JsonWriter writer, NbtValue value, JsonSerializerOptions options)
    {
        var nodes = 0;
        WriteValue(writer, value, options, 0, ref nodes);
    }
    private static void WriteValue(Utf8JsonWriter writer, NbtValue value, JsonSerializerOptions options, int depth, ref int nodes)
    {
        Budget(depth, ref nodes);
        writer.WriteStartObject();
        writer.WriteString("tag", Name(value.Type));
        if (value is NbtList declared) writer.WriteString("elementType", Name(declared.ElementType));
        writer.WritePropertyName("value");
        switch (value)
        {
            case NbtByte number: writer.WriteNumberValue(number.Value); break;
            case NbtShort number: writer.WriteNumberValue(number.Value); break;
            case NbtInt number: writer.WriteNumberValue(number.Value); break;
            case NbtLong number: writer.WriteStringValue(number.Value.ToString(CultureInfo.InvariantCulture)); break;
            case NbtFloat number: writer.WriteStringValue(unchecked((uint)number.RawBits).ToString("x8", CultureInfo.InvariantCulture)); break;
            case NbtDouble number: writer.WriteStringValue(unchecked((ulong)number.RawBits).ToString("x16", CultureInfo.InvariantCulture)); break;
            case NbtString text: JsonSerializer.Serialize(writer, text.Value, options); break;
            case NbtByteArray array:
                writer.WriteStartArray(); foreach (var number in array.Values) writer.WriteNumberValue(number); writer.WriteEndArray(); break;
            case NbtIntArray array:
                writer.WriteStartArray(); foreach (var number in array.Values) writer.WriteNumberValue(number); writer.WriteEndArray(); break;
            case NbtLongArray array:
                writer.WriteStartArray(); foreach (var number in array.Values) writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture)); writer.WriteEndArray(); break;
            case NbtList list:
                writer.WriteStartArray(); foreach (var item in list.Items) WriteValue(writer, item, options, depth + 1, ref nodes); writer.WriteEndArray(); break;
            case NbtCompound compound:
                writer.WriteStartArray();
                foreach (var entry in compound.Entries)
                {
                    writer.WriteStartObject(); writer.WritePropertyName("name"); JsonSerializer.Serialize(writer, entry.Name, options);
                    writer.WritePropertyName("value"); WriteValue(writer, entry.Value, options, depth + 1, ref nodes); writer.WriteEndObject();
                }
                writer.WriteEndArray(); break;
            default: throw new JsonException("Unknown NBT payload");
        }
        writer.WriteEndObject();
    }
}
