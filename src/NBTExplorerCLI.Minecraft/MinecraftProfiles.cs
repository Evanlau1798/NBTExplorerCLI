using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class MinecraftProfiles
{
    public static CapabilityProfile? ForId(string id) => id switch
    {
        "java-early-anvil" => EarlyAnvil, "java-early-anvil-json-names" => EarlyJsonNames, "java-1.13" => HistoricalPlainLore,
        "java-1.14.4" => HistoricalJson, "java-1.20.1" => Java1201, "java-1.20.4" => Java1204, "java-item-components-initial" => InitialItemComponents,
        "java-1.20.5" => Java1205, "java-1.21" => Java121, "java-1.21.5" => Java1215,
        "java-nbt-text-initial-hover" => InitialNativeText, "java-nbt-text-homogeneous" => EarlyNativeText,
        "java-nbt-text-mixed-lists" => MixedListText, "java-1.21.9" => Java1219,
        "java-1.21.11" => Java12111, "java-26.1-snapshot-6" => Snapshot2616, "java-classic-anvil" => ClassicAnvil,
        _ => ClassicProfiles.Values.FirstOrDefault(profile => profile.Id == id)
    };
    public static PackFormatVersion RequiredResourcePackFormat(CapabilityProfile profile, WorldMetadata? world)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (world?.Profile.Id == profile.Id) return RequiredResourcePackFormat(world.DataVersion);
        return RequiredResourcePackFormat(DataVersionRanges.FirstOrDefault(range => range.Profile.Id == profile.Id).DataVersion);
    }

    public static PackFormatVersion RequiredResourcePackFormat(int? dataVersion)
        => dataVersion switch
        {
            4536 => new(65, 1), 5023 => new(97, 1),
            _ => new(dataVersion switch
            {
                3463 or 3465 => 15, 3578 => 18, 3690 => 21, 3700 => 22, 3819 => 28, 3837 or 3839 => 32,
                3946 or 3947 or 3953 or 3955 => 34, 4189 => 46,
                4298 => 47, 4304 => 48, 4308 => 49, 4310 => 50, 4313 => 51, 4315 => 52, 4316 or 4317 => 53,
                4325 => 55, 4534 => 65, 4554 => 69,
                4671 => 75, 4774 => 80, 4786 => 84, 4903 => 88, 5130 => 104,
                _ => throw new CodecException(CodecError.UnsupportedProfile, 0, "The target world's resource pack format is not verified")
            }, 0)
        };
    public static CapabilityProfile? ForPackMetadata(PackMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Minimum is null || metadata.Maximum is null || metadata.Minimum.CompareTo(metadata.Maximum) > 0) return null;
        var version = metadata.IntegerFormat is { } formatNumber && formatNumber >= 0 ? new PackFormatVersion(formatNumber, 0) : metadata.Minimum;
        if (!metadata.DeclaresSupport(version)) return null;
        return (metadata.Kind, version.Major, version.Minor) switch
        {
            (PackSourceKind.Datapack, >= 4 and <= 23, 0) or (PackSourceKind.ResourcePack, >= 4 and <= 20, 0) => Java1201,
            (PackSourceKind.Datapack, >= 24 and <= 32, 0) or (PackSourceKind.ResourcePack, >= 21 and <= 27, 0) => Java1204,
            // Resource format 28 was shared before and after item conditions arrived.
            (PackSourceKind.Datapack, 33, 0) or (PackSourceKind.ResourcePack, 28, 0) => InitialItemComponents,
            (PackSourceKind.Datapack, >= 34 and <= 44, 0) or (PackSourceKind.ResourcePack, >= 29 and <= 33, 0) => Java1205,
            (PackSourceKind.Datapack, >= 45 and <= 61, 0) or (PackSourceKind.ResourcePack, >= 34 and <= 46, 0) => Java121,
            (PackSourceKind.Datapack, 62, 0) or (PackSourceKind.ResourcePack, 47, 0) => InitialNativeText,
            (PackSourceKind.Datapack, 63, 0) or (PackSourceKind.ResourcePack, 48, 0) => EarlyNativeText,
            // Resource format 53 is shared by both SNBT grammars. Use their common syntax without world evidence.
            (PackSourceKind.Datapack, >= 64 and <= 68, 0) or (PackSourceKind.ResourcePack, >= 49 and <= 53, 0) => MixedListText,
            (PackSourceKind.Datapack, >= 69 and <= 82, _) or (PackSourceKind.ResourcePack, >= 54 and <= 64, 0) or (PackSourceKind.ResourcePack, 65, 0) => Java1215,
            (PackSourceKind.Datapack, >= 83 and <= 93, _) or (PackSourceKind.ResourcePack, 65, > 0) or (PackSourceKind.ResourcePack, >= 66 and <= 74, _) => Java1219,
            (PackSourceKind.Datapack, >= 94 and <= 98, _) or (PackSourceKind.ResourcePack, >= 75 and <= 79, _) => Java12111,
            (PackSourceKind.Datapack, >= 99, _) or (PackSourceKind.ResourcePack, >= 80, _) => Snapshot2616,
            _ => null
        };
    }
    internal static CapabilityProfile EarlyAnvil { get; } = Create("java-early-anvil", "java-dimensions-directory", "java-item-tags-1.20.4", "java-early-anvil-text", "java-early-anvil-commands", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile EarlyJsonNames = Create("java-early-anvil-json-names", "java-dimensions-directory", "java-item-tags-1.20.4", "java-early-anvil-json-names", "java-early-anvil-commands", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile HistoricalPlainLore = Create("java-1.13", "java-dimensions-directory", "java-item-tags-1.20.4", "java-text-json", "java-commands-1.20.1", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile HistoricalJson = Create("java-1.14.4", "java-dimensions-directory", "java-item-tags-1.20.4", "java-text-json", "java-commands-1.20.1", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile Java1201 = Create("java-1.20.1", "java-dimensions-directory", "java-item-tags-1.20.4", "java-text-json", "java-commands-1.20.1", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile Java1204 = Create("java-1.20.4", "java-dimensions-directory", "java-item-tags-1.20.4", "java-text-json", "java-commands-1.20.4", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile InitialItemComponents = Create("java-item-components-initial", "java-dimensions-directory", "java-item-components-1.20.5", "java-text-json", "java-commands-item-components-initial", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile Java1205 = Create("java-1.20.5", "java-dimensions-directory", "java-item-components-1.20.5", "java-text-json", "java-commands-1.20.5", LogicalListEncoding.Homogeneous, "java-pack-plural");
    private static readonly CapabilityProfile Java121 = Create("java-1.21", "java-dimensions-directory", "java-item-components-1.20.5", "java-text-json", "java-commands-1.21", LogicalListEncoding.Homogeneous);
    private static readonly CapabilityProfile InitialNativeText = Create("java-nbt-text-initial-hover", "java-dimensions-directory", "java-item-components-1.21.5", "java-text-nbt", "java-commands-nbt-text-homogeneous", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile EarlyNativeText = Create("java-nbt-text-homogeneous", "java-dimensions-directory", "java-item-components-1.21.5", "java-text-nbt", "java-commands-nbt-text-homogeneous", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile MixedListText = Create("java-nbt-text-mixed-lists", "java-dimensions-directory", "java-item-components-1.21.5", "java-text-nbt", "java-commands-nbt-text-mixed-lists", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile Java1215 = Create("java-1.21.5", "java-dimensions-directory", "java-item-components-1.21.5", "java-text-nbt", "java-commands-1.21.5", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile Java1219 = Create("java-1.21.9", "java-dimensions-directory", "java-item-components-1.21.5", "java-text-nbt", "java-commands-1.21.5", LogicalListEncoding.CompoundWrapper, textVersion: 2);
    private static readonly CapabilityProfile Java12111 = Create("java-1.21.11", "java-dimensions-directory", "java-item-components-1.21.11", "java-text-nbt", "java-commands-1.21.11", LogicalListEncoding.CompoundWrapper, textVersion: 2);
    private static readonly CapabilityProfile Snapshot2616 = Create("java-26.1-snapshot-6", "java-dimensions-namespaced", "java-item-components-26.1-snapshot-6", "java-text-nbt", "java-commands-26.1-snapshot-6", LogicalListEncoding.CompoundWrapper, textVersion: 2);
    public static CapabilityProfile ClassicAnvil { get; } = Create("java-classic-anvil", "java-dimensions-directory", "java-classic-item-tags", "java-classic-stored-text", "java-classic-commands-unresolved", LogicalListEncoding.Homogeneous, "java-classic-packs-unresolved");
    internal static CapabilityProfile Java125 { get; } = Create("java-1.2.5", "java-dimensions-directory", "java-classic-item-tags", "java-classic-stored-text", "java-no-stored-commands-1.2.5", LogicalListEncoding.Homogeneous, "java-classic-packs-unresolved");
    private static readonly CapabilityProfile Java152 = Create("java-1.5.2", "java-dimensions-directory", "java-classic-item-tags", "java-classic-stored-text", "java-commands-1.5.2", LogicalListEncoding.Homogeneous, "java-classic-packs-unresolved");
    public static CapabilityProfile Java164 { get; } = Create("java-1.6.4", "java-dimensions-directory", "java-classic-item-tags", "java-classic-stored-text", "java-commands-1.6.4", LogicalListEncoding.Homogeneous, "java-classic-packs-unresolved");
    public static CapabilityProfile Java181 { get; } = Create("java-1.8.1", "java-dimensions-directory", "java-classic-item-tags", "java-text-json", "java-commands-1.8.1", LogicalListEncoding.Homogeneous, "java-classic-packs-unresolved");
    private static readonly Dictionary<string, CapabilityProfile> ClassicProfiles = new(StringComparer.Ordinal)
    {
        ["1.2.5"] = Java125,
        ["1.5.2"] = Java152,
        ["1.6.4"] = Java164,
        ["1.8.1"] = Java181
    };
    public static IEnumerable<string> ClassicGameVersions => ClassicProfiles.Keys;
    public static CapabilityProfile? ForClassicVersion(string version) => ClassicProfiles.GetValueOrDefault(version);
    public static string? ClassicVersion(string profile) => ClassicProfiles.FirstOrDefault(pair => pair.Value.Id == profile).Key;
    public static bool IsVersionedClassic(CapabilityProfile? profile) => profile is not null && ClassicProfiles.ContainsValue(profile);
    public static bool UsesClassicPlainText(CapabilityProfile? profile) => profile == Java125 || profile == Java152 || profile == Java164;
    public static bool IsReadOnlyAnvil(CapabilityProfile? profile) => profile == EarlyAnvil || profile == EarlyJsonNames;
    internal static bool UsesPlainItemLore(CapabilityProfile profile) => IsReadOnlyAnvil(profile) || profile == HistoricalPlainLore;

    public static ImmutableArray<(int DataVersion, int Minimum, int? Maximum, CapabilityProfile Profile)> DataVersionRanges { get; } =
    [
        // Separate vanilla data fixes cover stored names, scoreboard names and item lore.
        (169, 100, 1457, EarlyAnvil),
        (1513, 1458, 1513, EarlyJsonNames),
        (1631, 1514, 1802, HistoricalPlainLore),
        (1976, 1803, 3462, HistoricalJson),
        (3465, 3463, 3689, Java1201),
        (3700, 3690, 3818, Java1204),
        // 24w10a added item conditions, after item components. 24w21a added @n and singular pack directories.
        (3819, 3819, 3820, InitialItemComponents),
        (3837, 3821, 3945, Java1205),
        (3953, 3946, 4297, Java121),
        // Native text, the hover field rename, mixed lists and extended literals arrived separately.
        (4298, 4298, 4303, InitialNativeText),
        (4304, 4304, 4307, EarlyNativeText),
        (4308, 4308, 4316, MixedListText),
        (4325, 4317, 4535, Java1215),
        (4554, 4536, 4670, Java1219),
        (4671, 4671, 4773, Java12111),
        (4774, 4774, null, Snapshot2616)
    ];

    public static CapabilityProfile? ForDataVersion(int dataVersion)
    {
        foreach (var range in DataVersionRanges)
            if (dataVersion >= range.Minimum && (range.Maximum is null || dataVersion <= range.Maximum)) return range.Profile;
        return null;
    }

    public static SnbtProfile SnbtGrammar(CapabilityProfile profile)
        => profile == Java181 ? SnbtProfile.Java181 : profile == MixedListText ? SnbtProfile.JavaMixedLists
            : profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && profile != InitialNativeText && profile != EarlyNativeText
                ? SnbtProfile.Java1215 : SnbtProfile.JavaThrough1214;

    internal static string NativeHoverField(CapabilityProfile profile) => profile == InitialNativeText ? "text" : "value";
    internal static bool SupportsItemConditions(CapabilityProfile profile) => profile != InitialItemComponents
        && profile.Features[ProfileFacet.ItemSchema].Schema.StartsWith("java-item-components-", StringComparison.Ordinal);
    internal static bool SupportsNearestSelector(CapabilityProfile profile) => profile.Features[ProfileFacet.PackLayout].Schema == "java-pack-singular";

    internal static bool UsesCompoundEquipment(CapabilityProfile profile)
        => profile != InitialNativeText && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";

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
