using NBTExplorerCLI.Domain;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBTExplorerCLI.Formats;

internal static class ContractJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 1024
        };
        options.Converters.Add(new LosslessStringConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new HashJsonConverter());
        options.Converters.Add(new IdentityJsonConverter());
        options.Converters.Add(new AddressJsonConverter());
        options.Converters.Add(new PathJsonConverter());
        options.Converters.Add(new NbtContractJsonConverter());
        options.Converters.Add(new ArtifactJsonConverter());
        options.Converters.Add(new ImageJsonConverter());
        options.Converters.Add(new SlotJsonConverter());
        options.Converters.Add(new SessionSourceJsonConverter());
        options.Converters.Add(new EditJsonConverter());
        options.Converters.Add(new OperationJsonConverter());
        return options;
    }
    private sealed record Envelope<T>(string SchemaFamily, int SchemaVersion, T Data);
    public static string Write<T>(string family, T data) => JsonSerializer.Serialize(new Envelope<T>(family, 1, data), Options);
    internal static byte[] WriteUtf8<T>(string family, T data) => JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(family, 1, data), Options);
    internal static string WriteBounded<T>(string family, T data)
        => SerializeBounded(new Envelope<T>(family, 1, data));
    internal static string WriteForStorage<T>(string family, T data)
    {
        var json = WriteBounded(family, data);
        try { _ = Read(json, family, _ => true); }
        catch (JsonException error) { throw new JsonOutputLimitException("Contract output exceeds its decoding budget", error); }
        return json;
    }
    internal static string SerializeBounded<T>(T value, JsonSerializerOptions? options = null)
    {
        using var output = new LimitedJsonOutput();
        JsonSerializer.Serialize(output, value, options ?? Options);
        return Encoding.UTF8.GetString(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
    }
    private sealed class LimitedJsonOutput : MemoryStream
    {
        private void Check(int count) { if (count > 67_108_864 - Position) throw new JsonOutputLimitException(); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
    public static T Read<T>(string json, string family, Func<JsonElement, T> decode)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 67_108_864 || LosslessStringConverter.HasUnpairedSurrogate(json)
            || Encoding.UTF8.GetByteCount(json) > 67_108_864) throw new JsonException("Invalid or oversized contract JSON");
        return ReadBytes(Encoding.UTF8.GetBytes(json), family, decode);
    }
    internal static T ReadUtf8<T>(ReadOnlyMemory<byte> bytes, string family, Func<JsonElement, T> decode)
    {
        if (bytes.Length > 67_108_864) throw new JsonException("Invalid or oversized contract JSON");
        _ = StrictUtf8.GetCharCount(bytes.Span);
        return ReadBytes(bytes, family, decode);
    }
    private static T ReadBytes<T>(ReadOnlyMemory<byte> bytes, string family, Func<JsonElement, T> decode)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { MaxDepth = 1024 });
            var objects = new Stack<HashSet<string>>();
            var tokens = 0;
            while (reader.Read())
            {
                if (++tokens > 1_000_000) throw new JsonException("Contract token budget exceeded");
                if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!))
                    throw new JsonException("Duplicate contract member");
            }
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 1024 });
            var root = document.RootElement;
            Fields(root, "schemaFamily", "schemaVersion", "data");
            if (root.GetProperty("schemaFamily").ValueKind != JsonValueKind.String) throw new JsonException("Missing schema family");
            if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number) throw new JsonException("Missing schema version");
            if (root.GetProperty("schemaFamily").GetString() != family || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new NotSupportedException("Unsupported contract family/version");
            if (root.GetProperty("data").ValueKind != JsonValueKind.Object) throw new JsonException("Missing contract data");
            return decode(root.GetProperty("data"));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException or EditRejectedException)
        {
            throw new JsonException("Invalid contract data", error);
        }
    }
    public static T Decode<T>(JsonElement element) where T : class
        => element.Deserialize<T>(Options) ?? throw new JsonException("Missing contract value");
    public static T Required<T>(T? value) where T : class => value ?? throw new JsonException("Missing required contract member");
    public static void Fields(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != fields.Length
            || fields.Any(field => !element.TryGetProperty(field, out _))) throw new JsonException("Missing or unknown contract members");
    }
}

internal sealed class HashJsonConverter : JsonConverter<Hash256>
{
    public override Hash256 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("Missing hash"));
    public override void Write(Utf8JsonWriter writer, Hash256 hash, JsonSerializerOptions options) => writer.WriteStringValue(hash.Hex);
}
internal sealed class IdentityJsonConverter : JsonConverter<StableIdentity>
{
    private sealed record IdentityDto(IdentityKind? Kind, Hash256? Digest, byte[]? Canonical);
    public override StableIdentity Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<IdentityDto>(ref reader, options) ?? throw new JsonException("Missing identity");
        var identity = new StableIdentity(dto.Kind ?? throw new JsonException("Missing identity kind"),
            dto.Digest ?? throw new JsonException("Missing digest"), dto.Canonical ?? throw new JsonException("Missing canonical bytes"));
        return identity.IsValid ? identity : throw new JsonException("Invalid canonical identity");
    }
    public override void Write(Utf8JsonWriter writer, StableIdentity identity, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new IdentityDto(identity.Kind, identity.Digest, identity.Canonical.ToArray()), options);
}
internal sealed class AddressJsonConverter : JsonConverter<SourceAddress>
{
    public override SourceAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return SourceAddressJson.Read(document.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, SourceAddress address, JsonSerializerOptions options) => writer.WriteRawValue(SourceAddressJson.Write(address));
}
internal sealed class PathJsonConverter : JsonConverter<TypedPath>
{
    private sealed record SegmentDto(string? Kind, string? Key, int? Index);
    public override TypedPath Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var path = JsonSerializer.Deserialize<SegmentDto?[]>(ref reader, options) ?? throw new JsonException("Missing path");
        return new(path.Select(segment => segment switch
        {
            { Kind: "key", Key: not null, Index: null } => PathSegment.ForKey(segment.Key),
            { Kind: "index", Key: null, Index: not null } => PathSegment.ForIndex(segment.Index.Value),
            _ => throw new JsonException("Expected exactly one typed selector")
        }));
    }
    public override void Write(Utf8JsonWriter writer, TypedPath path, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, path.Segments.Select(segment => new SegmentDto(
            segment.Kind == PathSegmentKind.Key ? "key" : "index", segment.Key, segment.Index)).ToArray(), options);
}
