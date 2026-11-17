using Next.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Next.Formats.ContractJson;

namespace Next.Formats;

internal sealed class OperationJsonConverter : JsonConverter<PatchOperation>
{
    private sealed record TypedDto(SourceAddress? Address, TypedEdit? Edit);
    private sealed record LiteralDto(TextSlot? Slot, SlotReply? Reply);
    private sealed record KeyDto(SourceAddress? Address, NbtValue? Expected, KeyConversionRule? Rule, string? Profile,
        int? ProfileVersion, TextSlot? Slot, SlotReply? Reply, LanguageDependency? Dependency);
    private sealed record MemberDto(SourceAddress? Address, string? ExpectedText, string? ReplacementText, string? GrammarProfile, int? GrammarVersion);
    private sealed record ReferenceDto(StableIdentity? GroupId, long? GroupRevision, Hash256? MemberDigest,
        ReferenceCompleteness? Completeness, MemberDto?[]? Members);
    private sealed record RestoreDto(Hash256? VerifiedManifestHash, BackupArtifact[]? AuthorizedArtifacts, ArtifactPrecondition[]? CurrentImages);
    private sealed record ResourceDto(int? Version, Guid? WorldId, ResourceOutputFormat? Format, string? Destination,
        ResourceLanguage[]? Languages, PackFormatVersion? PackFormat, ResourceMerge? Merge);

    public override PatchOperation Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        Fields(root, "id", "kind", "payload");
        var id = root.GetProperty("id").Deserialize<string>(options) ?? throw new JsonException("Missing operation ID");
        var kind = root.GetProperty("kind").Deserialize<PatchKind>(options);
        var body = root.GetProperty("payload");
        return kind switch
        {
            PatchKind.TypedEdit => ReadTyped(id, Decode<TypedDto>(body)),
            PatchKind.LiteralTranslation => ReadLiteral(id, Decode<LiteralDto>(body)),
            PatchKind.TranslationKeyConversion => ReadKey(id, Decode<KeyDto>(body)),
            PatchKind.ReferenceSynchronization => ReadReferences(id, Decode<ReferenceDto>(body)),
            PatchKind.ArtifactRestore => ReadRestore(id, Decode<RestoreDto>(body)),
            PatchKind.ResourceGeneration => ReadResource(id, Decode<ResourceDto>(body)),
            _ => throw new JsonException("Unknown operation kind")
        };
    }
    private static TypedPatch ReadTyped(string id, TypedDto dto) => new(id, Required(dto.Address), Required(dto.Edit));
    private static LiteralPatch ReadLiteral(string id, LiteralDto dto) => new(id, new(Required(dto.Slot), Required(dto.Reply)));
    private static KeyConversionPatch ReadKey(string id, KeyDto dto) => new(id, new(Required(dto.Address), Required(dto.Expected),
        dto.Rule ?? throw new JsonException("Missing conversion rule"), Required(dto.Profile),
        dto.ProfileVersion ?? throw new JsonException("Missing profile version"), Required(dto.Slot), Required(dto.Reply), Required(dto.Dependency)));
    private static ReferencePatch ReadReferences(string id, ReferenceDto dto) => new(id, new(Required(dto.GroupId),
        dto.GroupRevision ?? throw new JsonException("Missing group revision"), Required(dto.MemberDigest),
        dto.Completeness ?? throw new JsonException("Missing coverage"), Required(dto.Members).Select(item =>
        {
            var member = Required(item);
            return new ReferenceMember(Required(member.Address), Required(member.ExpectedText), Required(member.ReplacementText),
                Required(member.GrammarProfile), member.GrammarVersion ?? throw new JsonException("Missing grammar version"));
        })));
    private static RestorePatch ReadRestore(string id, RestoreDto dto) => new(id, new(Required(dto.VerifiedManifestHash), Required(dto.AuthorizedArtifacts),
        Required(dto.CurrentImages).Select(item =>
        {
            var current = Required(item);
            return new KeyValuePair<ArtifactAddress, ArtifactImage>(Required(current.Artifact), Required(current.Image));
        })));

    private static ResourcePatch ReadResource(string id, ResourceDto dto)
    {
        var version = dto.Version ?? throw new JsonException("Missing resource generation version");
        if (version != ResourceGeneration.Version) throw new NotSupportedException("Unknown resource generation version");
        return new(id, new(dto.WorldId ?? throw new JsonException("Missing resource scope"), dto.Format ?? throw new JsonException("Missing resource format"),
            Required(dto.Destination), Required(dto.Languages), dto.PackFormat, dto.Merge));
    }
    public override void Write(Utf8JsonWriter writer, PatchOperation operation, JsonSerializerOptions options)
    {
        object payload = operation switch
        {
            TypedPatch typed => new TypedDto(typed.Address, typed.Edit),
            LiteralPatch literal => new LiteralDto(literal.Translation.Slot, literal.Translation.Reply),
            KeyConversionPatch key => new KeyDto(key.Conversion.Address, key.Conversion.Expected, key.Conversion.Rule,
                key.Conversion.Profile, key.Conversion.ProfileVersion, key.Conversion.Slot, key.Conversion.Reply, key.Conversion.Dependency),
            ReferencePatch reference => new ReferenceDto(reference.Synchronization.GroupId, reference.Synchronization.GroupRevision,
                reference.Synchronization.MemberDigest, reference.Synchronization.Completeness, reference.Synchronization.Members.Select(member =>
                    new MemberDto(member.Address, member.ExpectedText, member.ReplacementText, member.GrammarProfile, member.GrammarVersion)).ToArray()),
            RestorePatch restore => new RestoreDto(restore.Restore.VerifiedManifestHash,
                restore.Restore.AuthorizedArtifacts.OrderBy(item => item.Artifact.RelativePath, StringComparer.Ordinal).ToArray(),
                restore.Restore.CurrentImages.OrderBy(item => item.Key.RelativePath, StringComparer.Ordinal)
                    .Select(item => new ArtifactPrecondition(item.Key, item.Value)).ToArray()),
            ResourcePatch resource => new ResourceDto(ResourceGeneration.Version, resource.Generation.WorldId, resource.Generation.Format,
                resource.Generation.Destination, resource.Generation.Languages.ToArray(), resource.Generation.PackFormat, resource.Generation.Merge),
            _ => throw new JsonException("Unknown operation kind")
        };
        writer.WriteStartObject();
        writer.WritePropertyName("id"); JsonSerializer.Serialize(writer, operation.Id, options);
        writer.WritePropertyName("kind"); JsonSerializer.Serialize(writer, operation.Kind, options);
        writer.WritePropertyName("payload"); JsonSerializer.Serialize(writer, payload, payload.GetType(), options);
        writer.WriteEndObject();
    }
}

internal sealed class ArtifactJsonConverter : JsonConverter<ArtifactAddress>
{
    private sealed record ArtifactDto(Guid? WorldId, string? RelativePath, ArtifactKind? Kind, string? ContainerEntry);
    public override ArtifactAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<ArtifactDto>(ref reader, options) ?? throw new JsonException("Missing artifact");
        return new(dto.WorldId ?? throw new JsonException("Missing world"), Required(dto.RelativePath),
            dto.Kind ?? throw new JsonException("Missing artifact kind"), dto.ContainerEntry);
    }
    public override void Write(Utf8JsonWriter writer, ArtifactAddress value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new ArtifactDto(value.WorldId, value.RelativePath, value.Kind, value.ContainerEntry), options);
}
internal sealed class ImageJsonConverter : JsonConverter<ArtifactImage>
{
    private sealed record ImageDto(bool? ExpectedAbsent, Hash256? Hash);
    public override ArtifactImage Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<ImageDto>(ref reader, options) ?? throw new JsonException("Missing image");
        return dto switch
        {
            { ExpectedAbsent: true, Hash: null } => ArtifactImage.Absent,
            { ExpectedAbsent: false, Hash: not null } => ArtifactImage.Present(dto.Hash),
            _ => throw new JsonException("Present and Absent image fields are mutually exclusive")
        };
    }
    public override void Write(Utf8JsonWriter writer, ArtifactImage value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new ImageDto(value.ExpectedAbsent, value.Hash), options);
}
internal sealed class SlotJsonConverter : JsonConverter<TextSlot>
{
    public override TextSlot Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return TextSlotJson.Read(document.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, TextSlot slot, JsonSerializerOptions options) => writer.WriteRawValue(TextSlotJson.Write(slot));
}
internal sealed class EditJsonConverter : JsonConverter<TypedEdit>
{
    public override TypedEdit Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return TypedEditJson.Read(document.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, TypedEdit edit, JsonSerializerOptions options) => writer.WriteRawValue(TypedEditJson.Write(edit));
}
