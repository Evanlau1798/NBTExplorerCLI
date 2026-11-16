using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class EncodedKeyContractTests
{
    private static readonly ArtifactAddress Source = new(World, "level.dat", ArtifactKind.Nbt);
    private static readonly TypedPath Physical = new([PathSegment.ForKey("Name")]);
    private static readonly TypedPath Text = new([PathSegment.ForKey("text")]);
    private static TranslationKeyConversion Conversion(SourceAddress address, SourceAddress? leaf = null, NbtValue? expected = null)
    {
        var slot = new TextSlot(leaf ?? new(Source, Physical, "bossbar.name", componentPath: Text), "Hello", "bossbar.name", "en_us", "zh_tw", true, PlaceholderPolicy.None);
        return new(address, expected ?? new NbtString("{\"text\":\"Hello\"}"), KeyConversionRule.EncodedTextToTranslate, "java-1.21", 2, slot,
            new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, "resources/assets/test/lang/zh_tw.json", ArtifactKind.Json), null, "zh_tw", "test.hello", "你好"));
    }
    public static void OpaqueRecipeSurvivesPlanExchangeWithoutBecomingTypedEdit()
    {
        var conversion = Conversion(new(Source, Physical, "bossbar.name", componentPath: TypedPath.Root));
        var operation = new KeyConversionPatch("encoded", conversion); var raw = Hash256.Compute([1, 2, 3]);
        var snapshot = SourceIdentity.Snapshot(World, [new(Source, raw, SourceOutcome.Success)], SnapshotScope.All, new("1", "1", "1", "1"));
        var language = conversion.Dependency.LanguageArtifact;
        var plan = new PatchPlan(World, snapshot, "minecraft-known-2", "encoded-test", [operation], [new(Source, ArtifactImage.Present(raw)), new(language, ArtifactImage.Absent)],
            [new(Source, OutputIntent.Write, ["projection"]), new(language, OutputIntent.Write, ["projection"])], []);
        var restored = PatchPlanJson.Read(PatchPlanJson.Write(plan)); var recipe = ((KeyConversionPatch)restored.Operations.Single()).Conversion;
        Require(restored.PlanHash == plan.PlanHash && recipe.Rule == conversion.Rule && recipe.Expected == conversion.Expected && recipe.Address == conversion.Address
            && recipe.Slot.Id == conversion.Slot.Id && recipe.Reply == conversion.Reply && recipe.Dependency == conversion.Dependency,
            "encoded raw expected text, layered root/slot, profile and dependency retain their semantic plan hash");
        Reject<NotSupportedException>(() => conversion.Replacement());
        Reject<NotSupportedException>(() => TranslationKeyConversions.Apply(new(Source, TypedPath.Root, "source.document"), new("", new NbtCompound([new("Name", new NbtString("{\"text\":\"Hello\"}"))])), [conversion]));
    }
    public static void EncodedLayerAndComponentScopeCannotBeForged()
    {
        var root = new SourceAddress(Source, Physical, "bossbar.name", componentPath: TypedPath.Root);
        Reject<ArgumentException>(() => new KeyConversionPatch("wrong-type", Conversion(root, expected: new NbtCompound([]))));
        Reject<ArgumentException>(() => new KeyConversionPatch("physical", Conversion(new(Source, Physical, "bossbar.name"))));
        Reject<ArgumentException>(() => new KeyConversionPatch("role", Conversion(new(Source, Physical, "entity.name.hover", componentPath: TypedPath.Root))));
        Reject<ArgumentException>(() => new KeyConversionPatch("sibling", Conversion(root, new(Source, Physical, "bossbar.name", componentPath: new([PathSegment.ForKey("color")])))));
        Reject<ArgumentException>(() => new KeyConversionPatch("descendant", Conversion(root, new(Source, Physical, "bossbar.name", componentPath: new([PathSegment.ForKey("extra"), PathSegment.ForIndex(0), PathSegment.ForKey("text")])))));
        var movedLayer = new SourceAddress(Source, new(Physical.Segments.Concat(Text.Segments)), "bossbar.name");
        Reject<ArgumentException>(() => new KeyConversionPatch("layer", Conversion(root, movedLayer)));
        var hash = Hash256.Compute("{text:'Hello'}"u8).Hex;
        SyntaxLocator Span(string binding, TypedPath? path, int start = 0, int length = 14) => new("java-snbt-legacy", 1, start, length, binding, "utf8-java-string", "none", path);
        var encodedRoot = new SourceAddress(Source, Physical, "bossbar.name", syntax: Span(hash, TypedPath.Root));
        var encodedLeaf = new SourceAddress(Source, Physical, "bossbar.name", syntax: Span(hash, Text, 6, 7));
        _ = new KeyConversionPatch("syntax", Conversion(encodedRoot, encodedLeaf));
        Reject<ArgumentException>(() => new KeyConversionPatch("hash", Conversion(encodedRoot, new(Source, Physical, "bossbar.name", syntax: Span(Hash256.Compute([0]).Hex, Text, 6, 7)))));
        Reject<ArgumentException>(() => new KeyConversionPatch("span", Conversion(encodedRoot, new(Source, Physical, "bossbar.name", syntax: Span(hash, Text, 6, 14)))));
        Reject<ArgumentException>(() => new KeyConversionPatch("path", Conversion(new(Source, Physical, "bossbar.name", syntax: Span(hash, null)), encodedLeaf)));
    }
}
