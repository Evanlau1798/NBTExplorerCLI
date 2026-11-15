using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class KeyConversionTests
{
    private static readonly TypedPath ComponentPath = new([PathSegment.ForKey("name")]);
    private static readonly TypedPath TextPath = new([PathSegment.ForKey("name"), PathSegment.ForKey("text")]);
    private static NbtCompound Component => new([new("text", new NbtString("Hello %s")),
        new("color", new NbtString("red")), new("clickEvent", new NbtCompound([new("command", new NbtString("/say protected"))]))]);
    private static NbtDocument Document => new("root", new NbtCompound([new("name", Component), new("UUID", new NbtIntArray([1, 2, 3, 4]))]));
    private static TranslationKeyConversion Conversion()
    {
        var slot = new TextSlot(Address(TextPath), "Hello %s", "item.name", "en_us", "zh_tw", false, new(["%s"]));
        var pack = new ArtifactAddress(World, "packs/translated.zip", ArtifactKind.Archive);
        var language = new ArtifactAddress(World, pack.RelativePath, ArtifactKind.Archive, "assets/test/lang/zh_tw.json");
        return new(Address(ComponentPath), Component, KeyConversionRule.StructuredTextToTranslate, "synthetic.nbt-component", 1,
            slot, Reply(slot, "你好 %s"), new(language, pack, "zh_tw", "test.hello", "你好 %s"));
    }
    public static void ExactComponentConversion()
    {
        var conversion = Conversion();
        var source = Document;
        var candidate = TranslationKeyConversions.Apply(Address(TypedPath.Root), source, [conversion]);
        var expected = new NbtCompound([new("translate", new NbtString("test.hello")), new("color", new NbtString("red")),
            new("clickEvent", new NbtCompound([new("command", new NbtString("/say protected"))]))]);
        Require(NbtLocator.Resolve(candidate.Root, ComponentPath).Value == expected, "只把text改為translate，其他欄位與順序保留");
        Require(TranslationKeyConversions.Verify(Address(TypedPath.Root), source, candidate, [conversion]), "合法key conversion可完整驗證");
        var changedEvent = TypedTreeEdits.Apply(candidate, [TypedEdit.Replace(new([PathSegment.ForKey("name"),
            PathSegment.ForKey("clickEvent"), PathSegment.ForKey("command")]), new NbtString("/say protected"), new NbtString("evil"))]);
        Require(!TranslationKeyConversions.Verify(Address(TypedPath.Root), source, changedEvent, [conversion]), "即使在component授權範圍內仍拒絕額外event改動");
    }
    public static void RejectsInvalidConversions()
    {
        var conversion = Conversion();
        var unrelated = Slot();
        Reject(() => new TranslationKeyConversion(conversion.Address, Component, conversion.Rule, conversion.Profile, 1,
            unrelated, Reply(unrelated, "你好 %s"), conversion.Dependency).Replacement());
        var wrongDependency = new LanguageDependency(new(World, "assets/test/lang/en_us.json", ArtifactKind.Json), null,
            "en_us", "test.hello", "你好 %s");
        Reject(() => new TranslationKeyConversion(conversion.Address, Component, conversion.Rule, conversion.Profile, 1,
            conversion.Slot, conversion.Reply, wrongDependency).Replacement());
        var protectedWith = new NbtCompound(Component.Entries.Add(new("with", new NbtList(NbtType.String, [new NbtString("old")]))));
        Reject(() => new TranslationKeyConversion(conversion.Address, protectedWith, conversion.Rule, conversion.Profile, 1,
            conversion.Slot, conversion.Reply, conversion.Dependency).Replacement());
        Reject(() => new TranslationKeyConversion(conversion.Address, Component, (KeyConversionRule)99, conversion.Profile, 1,
            conversion.Slot, conversion.Reply, conversion.Dependency).Replacement());
        Reject(() => new LanguageDependency(new(World, "pack.zip", ArtifactKind.Archive, "assets/test/lang/zh_tw.json"),
            null, "zh_tw", "key", "text"));
        Reject(() => new LanguageDependency(new(World, "language.json", ArtifactKind.Json), new(World, "language.json", ArtifactKind.Archive),
            "zh_tw", "key", "text"));
    }

    public static void LiteralConversionAndDependencies()
    {
        var slot = new TextSlot(Address(ComponentPath), "Hello %s", "item.name", "en_us", "zh_tw", false, new(["%s"]));
        var dependency = new LanguageDependency(new(World, "assets/test/lang/zh_tw.json", ArtifactKind.Json), null,
            "zh_tw", "test.hello", "你好 %s");
        var conversion = new TranslationKeyConversion(slot.Address, new NbtString(slot.SourceText), KeyConversionRule.LiteralToTranslateComponent,
            "synthetic.nbt-literal", 1, slot, Reply(slot, dependency.Text), dependency);
        Require(conversion.Replacement() == new NbtCompound([new("translate", new NbtString("test.hello"))]), "literal-to-component必須由明示rule授權");
        Require(conversion.Dependency == dependency && conversion.Profile == "synthetic.nbt-literal", "plan需保存完整輸出依賴及profile版本");
        var wrong = new LanguageDependency(dependency.LanguageArtifact, null, "zh_tw", dependency.Key, "different %s");
        Reject(() => new TranslationKeyConversion(slot.Address, conversion.Expected, conversion.Rule, conversion.Profile, 1,
            slot, conversion.Reply, wrong).Replacement());
    }
}
