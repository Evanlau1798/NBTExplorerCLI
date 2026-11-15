using Next.Domain;
using Next.Formats;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

public static class NbtTextExtractor
{
    public static TextExtractionResult Extract(WorldSourceLocation source, NbtDocument document, ChunkPosition? chunk = null, DecodeLimits? limits = null,
        IReadOnlyList<StorageTextRule>? storageRules = null, SnapshotScope? scope = null, CapabilityProfile? layoutProfile = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(document);
        limits ??= DecodeLimits.Default; _ = BinaryNbt.MeasureDocument(document, limits);
        var versionPath = source.Role == WorldSourceRole.Level ? new TypedPath([PathSegment.ForKey("Data"), PathSegment.ForKey("DataVersion")]) : new([PathSegment.ForKey("DataVersion")]);
        var version = NbtLocator.Resolve(document.Root, versionPath);
        if (version.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid || version.Status == ResolutionStatus.Found && version.Value is not NbtInt)
            throw new CodecException(CodecError.InvalidTag, 0, "Invalid artifact DataVersion");
        var profile = version.Value is NbtInt number ? MinecraftProfiles.ForDataVersion(number.Value) : null;
        if (profile is null) throw new CodecException(CodecError.UnsupportedProfile, 0, "Missing or unknown artifact DataVersion");
        var binding = new SourceAddress(source.Artifact, TypedPath.Root, "source.document", source.Dimension, chunk);
        var output = new TextExtractionCollector(binding, false, limits, scope);
        var reader = new NbtTextReader(binding, profile, layoutProfile ?? profile, output, limits);
        switch (source.Role)
        {
            case WorldSourceRole.Player: reader.Player(document.Root, TypedPath.Root); break;
            case WorldSourceRole.Terrain: reader.Terrain(document.Root); break;
            case WorldSourceRole.Entities: reader.Entities(document.Root); break;
            case WorldSourceRole.Level: reader.Level(document.Root); break;
            case WorldSourceRole.SavedData: reader.SavedData(document.Root, source.Artifact.RelativePath, storageRules); break;
            case WorldSourceRole.PointOfInterest: break;
            case WorldSourceRole.Structure: reader.Structure(document.Root); break;
            default: output.Diagnostic(TypedPath.Root, "UnsupportedNbtTextSource"); break;
        }
        return output.Finish();
    }
}

internal sealed partial class NbtTextReader(SourceAddress binding, CapabilityProfile profile, CapabilityProfile layoutProfile, TextExtractionCollector output, DecodeLimits limits)
{
    private readonly bool modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
    private NbtValue? Field(NbtValue value, string key, TypedPath path)
    {
        var resolution = NbtLocator.Resolve(value, new([PathSegment.ForKey(key)]));
        if (resolution.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid) output.Diagnostic(At(path, key), "AmbiguousOrInvalidField");
        return resolution.Status == ResolutionStatus.Found ? resolution.Value : null;
    }
    private IEnumerable<(NbtValue Value, TypedPath Path)> Sequence(NbtValue? value, TypedPath path)
    {
        if (value is null) yield break;
        if (value is not NbtList list) { output.Diagnostic(path, "InvalidList"); yield break; }
        for (var index = 0; index < list.Items.Length; index++)
        {
            var child = list.Items[index]; var childPath = At(path, index);
            if (profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && profile.Lists.WrapperKey == "" && child is NbtCompound { Entries.Length: 1 } wrapper && wrapper.Entries[0].Name == "")
            { child = wrapper.Entries[0].Value; childPath = At(childPath, ""); }
            yield return (child, childPath);
        }
    }
    private void Component(NbtValue? value, TypedPath path, string role)
    {
        if (value is null || !output.CouldInclude(role)) return;
        var source = new SourceAddress(binding.Artifact, path, role, binding.Dimension, binding.Chunk);
        if (modern) output.Merge(TextComponentExtractor.Nbt(source, value, profile, limits, output.Scope));
        else if (value is NbtString json) output.Merge(TextComponentExtractor.Json(source, NativeJsonStrings.EncodeJavaDocument(json.Value, limits), profile, limits, output.Scope));
        else output.Diagnostic(path, "InvalidJsonComponentString");
    }
    private void Literal(NbtValue? value, TypedPath path, string role)
    {
        if (!output.Includes(role)) return;
        if (value is NbtString text) output.Literal(path, text.Value, role);
        else if (value is not null) output.Diagnostic(path, "InvalidString");
    }
    internal void Player(NbtValue value, TypedPath path)
    {
        foreach (var field in new[] { "Inventory", "EnderItems" })
            foreach (var (item, itemPath) in Sequence(Field(value, field, path), At(path, field))) Item(item, itemPath);
        Equipment(value, path);
    }
}
