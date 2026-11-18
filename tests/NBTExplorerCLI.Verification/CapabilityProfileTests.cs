using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Verification;

internal static class CapabilityProfileTests
{
    public static void IndependentCapabilities()
    {
        var features = new Dictionary<ProfileFacet, FormatFeature>
        {
            [ProfileFacet.Layout] = new("synthetic.layout", 1, new(Read: SupportLevel.Supported)),
            [ProfileFacet.TextSchema] = new("synthetic.text", 2,
                new(Read: SupportLevel.Supported, Extract: SupportLevel.Supported, Write: SupportLevel.Unsupported)),
            [ProfileFacet.Compression] = new("synthetic.compression", 3,
                new(Write: SupportLevel.Supported, Recover: SupportLevel.Supported))
        };
        var profile = new CapabilityProfile("synthetic", 7, features, new(LogicalListEncoding.CompoundWrapper, ""));
        Require(profile.Supports(ProfileOperation.Read, ProfileFacet.Layout, ProfileFacet.TextSchema),
            "已明示支援的必要 facets 應可讀取");
        Require(!profile.Supports(ProfileOperation.Write, ProfileFacet.Layout)
            && !profile.Supports(ProfileOperation.Write, ProfileFacet.TextSchema)
            && !profile.Supports(ProfileOperation.Read, ProfileFacet.ItemSchema)
            && !profile.Supports(ProfileOperation.Extract, ProfileFacet.CommandGrammar), "未知與明確不支援均不可推定允許");
        Require(!profile.Supports(ProfileOperation.Plan, ProfileFacet.TextSchema)
            && profile.Supports(ProfileOperation.Recover, ProfileFacet.Compression), "plan/recover 不可從 read/write 推導");
        features.Clear();
        Require(profile.Features.Count == 3 && profile.Features[ProfileFacet.TextSchema].Version == 2 && profile.Version == 7,
            "profile、facet 版本與呼叫端 mutable dictionary 必須分離");
    }

    public static void LogicalListBoundary()
    {
        var values = new NbtValue[] { new NbtInt(1), new NbtString("two") };
        var list = new LogicalNbtList(values);
        values[0] = new NbtInt(9);
        Require(list.Items[0] == new NbtInt(1) && list.Items[1] is NbtString, "logical list 應保存異型值且不可變");
        var wrapper = new ListMapping(LogicalListEncoding.CompoundWrapper, "");
        Require(wrapper.WrapperKey == "", "明示的空 wrapper key 合法，不代表任意 compound 可解包");
        Reject(() => new ListMapping(LogicalListEncoding.CompoundWrapper));
        Reject(() => new ListMapping(LogicalListEncoding.Homogeneous, ""));
        Reject(() => new LogicalNbtList([null!]));
        Require(new[] { DataSchemas.TypedNbt, DataSchemas.DisplayJson, DataSchemas.MinecraftText }.Distinct().Count() == 3,
            "保型、展示與文字 component 使用不同 schema");
    }

    public static void RejectsInvalidDeclarations()
    {
        var feature = new FormatFeature("synthetic", 1, new());
        Reject(() => new FormatFeature("", 1, new()));
        Reject(() => new FormatFeature("synthetic", 0, new()));
        Reject(() => new FormatFeature("synthetic", 1, new(Read: (SupportLevel)99)));
        Reject(() => new CapabilityProfile("synthetic", 1, [new((ProfileFacet)99, feature)], new(LogicalListEncoding.Unknown)));
        Reject(() => new CapabilityProfile("synthetic", 1, [new(ProfileFacet.Layout, null!)], new(LogicalListEncoding.Unknown)));
        var profile = new CapabilityProfile("synthetic", 1, [], new(LogicalListEncoding.Unknown));
        Reject(() => profile.Supports(ProfileOperation.Read));
        Reject(() => profile.Supports((ProfileOperation)99, ProfileFacet.Layout));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("非法能力宣告必須在 Domain 邊界拒絕");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
