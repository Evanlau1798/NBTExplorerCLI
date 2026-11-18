using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class TextComponentExtractionTests
{
    private static string Row(TextOccurrence text) => string.Join('|', text.Address.NodePath.ToJsonPointer(), text.Address.ComponentPath?.ToJsonPointer() ?? "<physical>", text.Address.SemanticRole, text.Text);
    public static void JsonProtectedFieldsAndFragments()
    {
        var source = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt), new([PathSegment.ForKey("CustomName")]), "entity.name");
        var json = Encoding.UTF8.GetBytes("{\"translate\":\"item.custom.visible\",\"fallback\":\"Fallback\",\"with\":[{\"text\":\"Argument\"},7,true],\"extra\":[\"Same\",{\"text\":\"Same\",\"bold\":true}],\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"Hover\"}},\"clickEvent\":{\"action\":\"run_command\",\"value\":\"/say protected\"},\"insertion\":\"protected\"}");
        var result = TextComponentExtractor.Json(source, json, MinecraftProfiles.ForDataVersion(3953)!);
        var expected = new[] {
            "/CustomName|/fallback|entity.name.fallback|Fallback", "/CustomName|/with/0/text|entity.name|Argument",
            "/CustomName|/extra/0|entity.name|Same", "/CustomName|/extra/1/text|entity.name|Same",
            "/CustomName|/hoverEvent/contents/text|entity.name.hover|Hover" };
        Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)), "exact embedded component slots, styled fragments and permitted hover only; numeric arguments/click/insertion protected");
        Require(result.TranslationKeys.Single().Key == "item.custom.visible" && result.TranslationKeys.Single().Address.ComponentPath?.ToJsonPointer() == "/translate", "translation key is separate exact reference, never a literal slot");
        Require(result.Occurrences.Select(text => text.ToSlot("en_us", "zh_tw").Id).Distinct().Count() == expected.Length && result.Occurrences.All(text => text.Type == NbtType.String), "same text in different paths keeps distinct typed slots");
        var pure = TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("{\"translate\":\"block.test.key\"}"), MinecraftProfiles.ForDataVersion(3953)!);
        Require(pure.Complete && pure.Occurrences.IsEmpty && pure.TranslationKeys.Length == 1, "pure translation component has no literal text");
    }
    public static void NbtPhysicalWrappersAndModernHover()
    {
        var source = new SourceAddress(new(World, "entities/r.-1.0.mca", ArtifactKind.Entities), new([PathSegment.ForKey("CustomName")]), "entity.name", "minecraft:overworld", new(-32, 0));
        var component = new NbtCompound([
            new("text", new NbtString("Root")), new("extra", new NbtList(NbtType.Compound, [new NbtCompound([new("", new NbtString("Scalar"))]), new NbtCompound([new("text", new NbtString("Styled")), new("color", new NbtString("red"))])])),
            new("hover_event", new NbtCompound([new("action", new NbtString("show_text")), new("value", new NbtCompound([new("text", new NbtString("Hover"))]))])),
            new("click_event", new NbtCompound([new("action", new NbtString("run_command")), new("command", new NbtString("say protected"))]))]);
        var profile = MinecraftProfiles.ForDataVersion(4774)!; var result = TextComponentExtractor.Nbt(source, component, profile);
        var expected = new[] { "/CustomName/text|<physical>|entity.name|Root", "/CustomName/extra/0/|<physical>|entity.name|Scalar",
            "/CustomName/extra/1/text|<physical>|entity.name|Styled", "/CustomName/hover_event/value/text|<physical>|entity.name.hover|Hover" };
        Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)), "NBT component locations include real heterogeneous-list wrapper keys, with correct modern hover schema");
        var document = new NbtCompound([new("CustomName", component)]);
        Require(result.Occurrences.All(text => NbtLocator.Resolve(document, text.Address.NodePath).Value == new NbtString(text.Text) && text.Address.Chunk == source.Chunk && text.Address.Dimension == source.Dimension), "every produced NBT address resolves to exact physical String without dropping dimension/chunk");
        var literal = TextComponentExtractor.Nbt(source, new NbtString("{\"text\":\"literal JSON spelling\"}"), profile);
        Require(literal.Occurrences.Single().Text == "{\"text\":\"literal JSON spelling\"}" && literal.Occurrences.Single().Address.NodePath.Equals(source.NodePath), "modern NBT string is literal text, never reparsed as embedded JSON");
    }
    public static void NativeJsonScalarArrayAndEmpty()
    {
        var source = new SourceAddress(new(World, "datapacks/p/data/models/advancement/start.json", ArtifactKind.Json), new([PathSegment.ForKey("display"), PathSegment.ForKey("title")]), "advancement.title");
        var result = TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("[\"\",{\"text\":\"\\ud800\",\"hover_event\":{\"action\":\"show_text\",\"value\":\"Hover\"}}]"), MinecraftProfiles.ForDataVersion(4325)!);
        Require(result.Complete && result.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
            "/display/title/0|<physical>|advancement.title|", "/display/title/1/text|<physical>|advancement.title|\ud800",
            "/display/title/1/hover_event/value|<physical>|advancement.title.hover|Hover" }.Order(StringComparer.Ordinal)), "native JSON keeps exact base path, modern events, empty scalar and Java UTF16");
        Require(result.Occurrences.All(text => text.AllowedEmpty), "component literals retain explicit empty-string policy");
    }
    public static void AmbiguityProtectionAndLimits()
    {
        var source = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt), TypedPath.Root, "entity.name");
        var profile = MinecraftProfiles.ForDataVersion(4774)!;
        var ambiguous = TextComponentExtractor.Nbt(source, new NbtCompound([new("text", new NbtString("One")), new("text", new NbtString("Two"))]), profile);
        Require(!ambiguous.Complete && ambiguous.Occurrences.IsEmpty && ambiguous.Diagnostics.Single().Code == "AmbiguousComponent", "duplicate physical NBT component members cannot authorize ambiguous slots");
        var conflicting = TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("{\"text\":\"One\",\"translate\":\"key\"}"), MinecraftProfiles.ForDataVersion(3953)!);
        Require(!conflicting.Complete && conflicting.Occurrences.IsEmpty && conflicting.TranslationKeys.IsEmpty, "conflicting component kinds remain unmodified diagnostics");
        var native = new SourceAddress(new(World, "pack.json", ArtifactKind.Json), TypedPath.Root, "advancement.title");
        var protectedHover = TextComponentExtractor.Json(native, Encoding.UTF8.GetBytes("{\"text\":\"Visible\",\"hover_event\":{\"action\":\"show_item\",\"id\":\"minecraft:stone\",\"value\":\"Protected\"}}"), profile);
        Require(protectedHover.Complete && protectedHover.Occurrences.Single().Text == "Visible", "only explicit show_text hover content is translatable");
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => TextComponentExtractor.Nbt(source, new NbtString("Hi"), MinecraftProfiles.ForDataVersion(3953)!));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("\"Hi\""), profile));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => TextComponentExtractor.Json(native, Encoding.UTF8.GetBytes("\"long\""), profile, new(MaxStringBytes: 2)));
    }
    public static void KeybindValueHoverAndSnapshotObjects()
    {
        var source = new SourceAddress(new(World, "data.json", ArtifactKind.Json), TypedPath.Root, "text_display.text");
        var usingValue = TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("{\"keybind\":\"key.jump\",\"extra\":[{\"text\":\"Press\"}],\"hoverEvent\":{\"action\":\"show_text\",\"value\":{\"text\":\"Hover from value\"}}}"), MinecraftProfiles.ForDataVersion(3953)!);
        Require(usingValue.Complete && usingValue.Occurrences.Select(text => text.Address.NodePath.ToJsonPointer() + "|" + text.Text).Order(StringComparer.Ordinal).SequenceEqual(new[] {
            "/extra/0/text|Press", "/hoverEvent/value/text|Hover from value" }.Order(StringComparer.Ordinal)), "keybind identity remains protected while extra and deprecated show_text using value value are extracted");
        var sprite = Encoding.UTF8.GetBytes("{\"atlas\":\"minecraft:blocks\",\"sprite\":\"item/stone\",\"extra\":[{\"text\":\"Label\"}]}");
        var snapshot = TextComponentExtractor.Json(source, sprite, MinecraftProfiles.ForDataVersion(4774)!);
        Require(snapshot.Complete && snapshot.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/extra/0/text" && snapshot.Occurrences.Single().Text == "Label", "snapshot6 implicit atlas object has protected resource IDs and extractable styled extra");
        var earlier = TextComponentExtractor.Json(source, sprite, MinecraftProfiles.ForDataVersion(4325)!);
        Require(!earlier.Complete && earlier.Occurrences.IsEmpty, "object-component support does not leak backwards into 1.21.5 profile");
        var known = MinecraftProfiles.ForDataVersion(4774)!;
        var unknownFeatures = known.Features.SetItem(ProfileFacet.TextSchema, new("java-text-nbt", 999, new(Read: SupportLevel.Supported)));
        var unknown = new CapabilityProfile("unknown", 1, unknownFeatures, known.Lists);
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => TextComponentExtractor.Json(source, sprite, unknown));
    }
    public static void UnknownObjectKindCannotAuthorizeExtra()
    {
        var source = new SourceAddress(new(World, "data.json", ArtifactKind.Json), TypedPath.Root, "text_display.text");
        var result = TextComponentExtractor.Json(source, Encoding.UTF8.GetBytes("{\"object\":\"future_kind\",\"extra\":[{\"text\":\"Unclassified\"}]}"), MinecraftProfiles.ForDataVersion(4774)!);
        Require(!result.Complete && result.Occurrences.IsEmpty && result.Diagnostics.Single().Code == "UnknownComponent", "unknown object kind must not become a complete known parent for literal extra");
    }
}
