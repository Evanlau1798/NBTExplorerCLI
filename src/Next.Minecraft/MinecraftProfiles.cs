using Next.Domain;

namespace Next.Minecraft;

public static class MinecraftProfiles
{
    private static readonly CapabilityProfile Java121 = Create("java-1.21", "java-dimensions-legacy", "java-item-components-1.20.5", "java-text-json", "java-commands-1.21", LogicalListEncoding.Homogeneous);
    private static readonly CapabilityProfile Java1215 = Create("java-1.21.5", "java-dimensions-legacy", "java-item-components-1.21.5", "java-text-nbt", "java-commands-1.21.5", LogicalListEncoding.CompoundWrapper);
    private static readonly CapabilityProfile Snapshot2616 = Create("java-26.1-snapshot-6", "java-dimensions-namespaced", "java-item-components-26.1-snapshot-6", "java-text-nbt", "java-commands-26.1-snapshot-6", LogicalListEncoding.CompoundWrapper);
    public static CapabilityProfile? ForDataVersion(int dataVersion)
        => dataVersion switch { 3953 => Java121, 4325 => Java1215, 4774 => Snapshot2616, _ => null };

    private static CapabilityProfile Create(string id, string layout, string items, string text, string commands, LogicalListEncoding lists)
        => new(id, 1, new Dictionary<ProfileFacet, FormatFeature>
        {
            [ProfileFacet.Layout] = Readable(layout),
            [ProfileFacet.ItemSchema] = Readable(items),
            [ProfileFacet.TextSchema] = Readable(text),
            [ProfileFacet.CommandGrammar] = new(commands, 1, new()),
            [ProfileFacet.Compression] = Readable("java-inline-compression")
        }, new(lists, lists == LogicalListEncoding.CompoundWrapper ? "" : null));
    private static FormatFeature Readable(string schema) => new(schema, 1, new(Read: SupportLevel.Supported));
}
