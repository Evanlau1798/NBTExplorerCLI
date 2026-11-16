using Next.Domain;

namespace Next.Minecraft;

public static class MinecraftProfiles
{
    public static CapabilityProfile? ForId(string id) => id switch
    {
        "java-1.20.4" => Java1204, "java-1.21" => Java121, "java-1.21.5" => Java1215,
        "java-26.1-snapshot-6" => Snapshot2616, _ => null
    };
    public static PackFormatVersion RequiredResourcePackFormat(CapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new(profile.Id switch
        {
            "java-1.20.4" => 22, "java-1.21" => 34, "java-1.21.5" => 55, "java-26.1-snapshot-6" => 80,
            _ => throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown required pack format")
        }, 0);
    }
    public static CapabilityProfile? ForPackMetadata(PackMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Minimum is null || metadata.Maximum is null || metadata.Minimum.CompareTo(metadata.Maximum) > 0) return null;
        var version = metadata.LegacyFormat is { } legacy && legacy >= 0 ? new PackFormatVersion(legacy, 0) : metadata.Minimum;
        if (version.Minor != 0 || !metadata.DeclaresSupport(version)) return null;
        return (metadata.Kind, version.Major) switch
        {
            (PackSourceKind.Datapack, 26) or (PackSourceKind.ResourcePack, 22) => Java1204,
            (PackSourceKind.Datapack, 48) or (PackSourceKind.ResourcePack, 34) => Java121,
            (PackSourceKind.Datapack, 71) or (PackSourceKind.ResourcePack, 55) => Java1215,
            (PackSourceKind.Datapack, 99) or (PackSourceKind.ResourcePack, 80) => Snapshot2616,
            _ => null
        };
    }
    private static readonly CapabilityProfile Java1204 = Create("java-1.20.4", "java-dimensions-legacy", "java-item-tags-1.20.4", "java-text-json", "java-commands-1.20.4", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile Java121 = Create("java-1.21", "java-dimensions-legacy", "java-item-components-1.20.5", "java-text-json", "java-commands-1.21", LogicalListEncoding.Homogeneous);
    private static readonly CapabilityProfile Java1215 = Create("java-1.21.5", "java-dimensions-legacy", "java-item-components-1.21.5", "java-text-nbt", "java-commands-1.21.5", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile Snapshot2616 = Create("java-26.1-snapshot-6", "java-dimensions-namespaced", "java-item-components-26.1-snapshot-6", "java-text-nbt", "java-commands-26.1-snapshot-6", LogicalListEncoding.CompoundWrapper, textVersion: 2);
    public static CapabilityProfile? ForDataVersion(int dataVersion)
        => dataVersion switch { 3700 => Java1204, 3953 => Java121, 4325 => Java1215, 4774 => Snapshot2616, _ => null };

    private static CapabilityProfile Create(string id, string layout, string items, string text, string commands, LogicalListEncoding lists, string packLayout = "java-pack-singular", int textVersion = 1)
        => new(id, 2, new Dictionary<ProfileFacet, FormatFeature>
        {
            [ProfileFacet.Layout] = Readable(layout),
            [ProfileFacet.ItemSchema] = Readable(items),
            [ProfileFacet.TextSchema] = new(text, textVersion, new(Read: SupportLevel.Supported)),
            [ProfileFacet.CommandGrammar] = new(commands, 1, new()),
            [ProfileFacet.Compression] = Readable("java-inline-compression"),
            [ProfileFacet.PackLayout] = Readable(packLayout)
        }, new(lists, lists == LogicalListEncoding.CompoundWrapper ? "" : null));
    private static FormatFeature Readable(string schema) => new(schema, 1, new(Read: SupportLevel.Supported));
}
