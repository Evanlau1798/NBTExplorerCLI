using Next.Domain;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Next.Formats;

public static class SourceAddressJson
{
    public const string Family = "nbtcli.next.source-address";
    public const int Version = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64,
        Converters = { new LosslessStringConverter() }
    };
    private sealed record ArtifactDto(Guid WorldId, string? RelativePath, string? Kind, string? ContainerEntry);
    private sealed record SegmentDto(string? Kind, string? Key, int? Index);
    private sealed record ChunkDto(int? X, int? Z);
    private sealed record SyntaxDto(string? GrammarProfile, int? GrammarVersion, long? StartByte, long? LengthBytes,
        string? PreimageHash, string? Encoding, string? Newline);
    private sealed record AddressDto(string? SchemaFamily, int SchemaVersion, ArtifactDto? Artifact,
        SegmentDto?[]? NodePath, string? SemanticRole, string? Dimension, ChunkDto? Chunk,
        SegmentDto?[]? ComponentPath, SyntaxDto? Syntax, string? JsonTarget);

    public static string Write(SourceAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return JsonSerializer.Serialize(new AddressDto(Family, Version,
            new(address.Artifact.WorldId, address.Artifact.RelativePath, address.Artifact.Kind.ToString().ToLowerInvariant(),
                address.Artifact.ContainerEntry), EncodePath(address.NodePath), address.SemanticRole, address.Dimension,
            address.Chunk is null ? null : new(address.Chunk.X, address.Chunk.Z),
            address.ComponentPath is null ? null : EncodePath(address.ComponentPath), address.Syntax is null ? null
                : new(address.Syntax.GrammarProfile, address.Syntax.GrammarVersion, address.Syntax.StartByte,
                    address.Syntax.LengthBytes, address.Syntax.PreimageHash, address.Syntax.Encoding, address.Syntax.Newline),
            address.JsonTarget == JsonTargetKind.Value ? "value" : "propertyName"), Options);
    }

    public static SourceAddress Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 67_108_864 || Encoding.UTF8.GetByteCount(json) > 67_108_864)
            throw new JsonException("Address JSON exceeds 64 MiB");
        if (LosslessStringConverter.HasUnpairedSurrogate(json)) throw new JsonException("Invalid Unicode JSON source");
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        var tokens = 0;
        var members = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (++tokens > 8192) throw new JsonException("Address token budget exceeded");
            if (reader.TokenType == JsonTokenType.StartObject) members.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) members.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string name;
                try { name = reader.GetString()!; }
                catch (InvalidOperationException error) { throw new JsonException("Invalid property name", error); }
                if (!members.Peek().Add(name)) throw new JsonException("Duplicate address member");
            }
        }
        var dto = JsonSerializer.Deserialize<AddressDto>(bytes, Options) ?? throw new JsonException("Missing address");
        if (dto.SchemaFamily is null || dto.SchemaVersion == 0) throw new JsonException("Missing schema identity");
        if (dto.SchemaFamily != Family || dto.SchemaVersion != Version) throw new NotSupportedException("Unsupported address schema");
        if (dto.Artifact is null || dto.Artifact.RelativePath is null || dto.Artifact.Kind is null
            || dto.NodePath is null || dto.SemanticRole is null) throw new JsonException("Missing required address fields");
        if (!Enum.TryParse<ArtifactKind>(dto.Artifact.Kind, true, out var kind) || !Enum.IsDefined(kind)
            || kind.ToString().ToLowerInvariant() != dto.Artifact.Kind) throw new JsonException("Invalid artifact kind");
        var target = dto.JsonTarget switch
        {
            "value" => JsonTargetKind.Value,
            "propertyName" => JsonTargetKind.PropertyName,
            _ => throw new JsonException("Invalid or missing JSON target kind")
        };
        try
        {
            return new(new(dto.Artifact.WorldId, dto.Artifact.RelativePath, kind, dto.Artifact.ContainerEntry),
                DecodePath(dto.NodePath), dto.SemanticRole, dto.Dimension, dto.Chunk is null ? null
                    : new(dto.Chunk.X ?? throw new JsonException("Missing chunk x"), dto.Chunk.Z ?? throw new JsonException("Missing chunk z")),
                dto.ComponentPath is null ? null : DecodePath(dto.ComponentPath), DecodeSyntax(dto.Syntax), target);
        }
        catch (ArgumentException error) { throw new JsonException("Invalid address", error); }
    }

    private static SegmentDto[] EncodePath(TypedPath path) => path.Segments.Select(segment =>
        new SegmentDto(segment.Kind == PathSegmentKind.Key ? "key" : "index", segment.Key, segment.Index)).ToArray();

    private static TypedPath DecodePath(SegmentDto?[] path) => new(path.Select(segment => segment switch
    {
        { Kind: "key", Key: not null, Index: null } => PathSegment.ForKey(segment.Key),
        { Kind: "index", Key: null, Index: not null } => PathSegment.ForIndex(segment.Index.Value),
        _ => throw new JsonException("Path segment must be exactly key or index")
    }));

    private static SyntaxLocator? DecodeSyntax(SyntaxDto? syntax) => syntax is null ? null : new(
        syntax.GrammarProfile ?? throw new JsonException("Missing grammar profile"),
        syntax.GrammarVersion ?? throw new JsonException("Missing grammar version"),
        syntax.StartByte ?? throw new JsonException("Missing syntax start byte"),
        syntax.LengthBytes ?? throw new JsonException("Missing syntax length"),
        syntax.PreimageHash ?? throw new JsonException("Missing syntax preimage"),
        syntax.Encoding ?? throw new JsonException("Missing syntax encoding"),
        syntax.Newline ?? throw new JsonException("Missing syntax newline"));
}
