using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Next.Domain;

namespace Next.Formats;

internal sealed class SessionSourceJsonConverter : JsonConverter<SessionSourceSnapshot>
{
    private sealed record SourceDto(Guid WorldId, StableIdentity SnapshotId, ImmutableArray<ManifestSource> Sources,
        ImmutableArray<string> Dimensions, ImmutableArray<string> Regions, ImmutableArray<string> Types, ImmutableArray<string> ArtifactPaths,
        InterpretationVersions Versions, bool CoverageComplete,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<SessionGroupRoot>? ContextRoots = null);
    public override SessionSourceSnapshot Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var data = document.RootElement;
        var fields = new[] { "worldId", "snapshotId", "sources", "dimensions", "regions", "types", "artifactPaths", "versions", "coverageComplete" };
        ContractJson.Fields(data, data.TryGetProperty("contextRoots", out _) ? fields.Append("contextRoots").ToArray() : fields);
        var dto = ContractJson.Decode<SourceDto>(data);
        if (data.TryGetProperty("contextRoots", out _) && dto.ContextRoots is null) throw new JsonException("Missing declared context roots");
        return new(dto.WorldId, dto.SnapshotId, dto.Sources, dto.Dimensions, dto.Regions, dto.Types, dto.ArtifactPaths, dto.Versions, dto.CoverageComplete, dto.ContextRoots ?? default);
    }
    public override void Write(Utf8JsonWriter writer, SessionSourceSnapshot value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new SourceDto(value.WorldId, value.SnapshotId, value.Sources, value.Dimensions, value.Regions, value.Types, value.ArtifactPaths, value.Versions, value.CoverageComplete,
            value.ContextRoots.IsDefault ? null : value.ContextRoots), options);
}
