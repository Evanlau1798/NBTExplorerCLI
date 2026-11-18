using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Verification;

internal static class NativeReferenceTests
{
    private static readonly Guid World = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static SourceAddress Root => new(new(World, "entity.dat", ArtifactKind.Nbt), new([PathSegment.ForKey("CustomName")]), "entity.name");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void SelectorTokensPreserveExactLocations()
    {
        var value = "@e[type=minecraft:armor_stand, tag=guard, name = !'A\\' B']";
        var parsed = SelectorNames.Parse(value);
        Require(parsed.Complete && parsed.Kind == 'e' && parsed.EntityType == "minecraft:armor_stand" && parsed.Names.Length == 1, "static selector grammar exposes its typed target and exact name member");
        var token = parsed.Names.Single();
        Require(token.Name == "A' B" && token.Negated && token.Member == 2 && value.Substring(token.Start, token.Length) == "'A\\' B'", "decoded names retain the original quoted token range without the negation operator");
        foreach (var selector in new[] { "@e[nbt={CustomName:'x'}]", "@e[future=x]", "@e[name=A,name=B]", "@x[name=A]", "@e[name='broken]", "@e[name=A] tail", "@e[tag={a:[1,2}]", "@e[name=A,,tag=x]" })
            Require(!SelectorNames.Parse(selector).Complete, "unknown/dynamic/malformed selectors cannot claim complete coverage: " + selector);
        Require(SelectorNames.Parse("@a[name=Player]").Kind == 'a', "player selectors remain distinguishable from entity name selectors");
        Require(SelectorNames.Parse("@e[tag=guard]").Complete && SelectorNames.Parse("@e[tag=guard]").Names.IsEmpty, "absence of name references is a valid parsed result");
    }
    public static void NativeSelectorsUseOriginalAddresses()
    {
        foreach (var version in new[] { 3953, 4325, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            ComponentReferenceResult result;
            if (version == 3953)
                result = NativeComponentReferences.EmbeddedJson(Root, "{\"text\":\"@e[name=Literal]\",\"extra\":[{\"selector\":\"@e[name=Guard]\"}]}", profile);
            else
                result = NativeComponentReferences.Nbt(Root, new NbtCompound([
                    new("text", new NbtString("@e[name=Literal]")),
                    new("extra", new NbtList(NbtType.Compound, [new NbtCompound([new("selector", new NbtString("@e[name=Guard]"))])]))]), profile);
            Require(result.Completeness == ReferenceCompleteness.Complete && result.Selectors.Length == 1 && result.Selectors[0].Parsed.Names.Single().Name == "Guard", "native semantic component roots find selectors and preserve same-spelled literal text");
            var address = result.Selectors[0].Address;
            var relative = new TypedPath([PathSegment.ForKey("extra"), PathSegment.ForIndex(0), PathSegment.ForKey("selector")]);
            Require(address.Artifact == Root.Artifact && address.Syntax is null && address.SemanticRole == "reference.component.selector"
                && (version == 3953 ? address.NodePath.Equals(Root.NodePath) && address.ComponentPath!.Equals(relative)
                    : address.ComponentPath is null && address.NodePath.Equals(new TypedPath(Root.NodePath.Segments.Concat(relative.Segments)))), "native references keep exact NBT/embedded JSON paths, never invented command syntax addresses");
        }
    }
    public static void DynamicNativeComponentsRemainUnresolved()
    {
        foreach (var version in new[] { 3953, 4325, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            foreach (var field in new[] { "nbt", "score", version == 3953 ? "clickEvent" : "click_event" })
            {
                var text = field == "nbt" ? "{\"nbt\":\"x\",\"entity\":\"@e\"}" : field == "score" ? "{\"score\":{\"name\":\"Guard\",\"objective\":\"x\"}}" : "{\"text\":\"x\",\"" + field + "\":{\"action\":\"run_command\",\"value\":\"kill @e[name=Guard]\"}}";
                var component = field == "nbt" ? new NbtCompound([new("nbt", new NbtString("x")), new("entity", new NbtString("@e"))])
                    : field == "score" ? new NbtCompound([new("score", new NbtCompound([new("name", new NbtString("Guard")), new("objective", new NbtString("x"))]))])
                    : new NbtCompound([new("text", new NbtString("x")), new(field, new NbtCompound([new("action", new NbtString("run_command")), new("value", new NbtString("kill @e[name=Guard]"))]))]);
                var result = version == 3953 ? NativeComponentReferences.EmbeddedJson(Root, text, profile) : NativeComponentReferences.Nbt(Root, component, profile);
                Require(result.Completeness == ReferenceCompleteness.Unresolved && result.Diagnostics.Length != 0, "native dynamic component must block complete reference authorization: " + field);
            }
        }
    }
    public static void SelectorResultRetentionIsBounded()
    {
        var input = "@e[" + string.Join(',', Enumerable.Repeat("future=x", 60)) + "]";
        try { _ = SelectorNames.Parse(input, new(MaxDecodedBytes: 2048)); }
        catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { return; }
        throw new InvalidOperationException("small selector text cannot allocate an oversized diagnostic result before the caller can charge it");
    }
    public static void JsonComponentsRetainTheirNativePathsAndJavaStrings()
    {
        foreach (var version in new[] { 3953, 4325, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            var source = new SourceAddress(new(World, "datapacks/test/data/test/advancement/a.json", ArtifactKind.Json), new([PathSegment.ForKey("display"), PathSegment.ForKey("title")]), "advancement.title");
            var bytes = NativeJsonStrings.EncodeJavaDocument("{\"translate\":\"key\",\"with\":[{\"selector\":\"@e[name='G\\ud800']\"}]}");
            var result = NativeComponentReferences.Json(source, bytes, profile);
            var reference = result.Selectors.Single();
            Require(result.Completeness == ReferenceCompleteness.Complete && reference.Parsed.Names.Single().Name == "G\ud800"
                && reference.Address.NodePath.Equals(new TypedPath(source.NodePath.Segments.Concat(new[] { PathSegment.ForKey("with"), PathSegment.ForIndex(0), PathSegment.ForKey("selector") })))
                && reference.Address.ComponentPath is null && reference.Address.Syntax is null,
                "native JSON components retain original document paths and lossless Java UTF-16 names");
        }
    }
    public static void DuplicateHoverActionsRemainDiagnosable()
    {
        var hover = new NbtCompound([new("action", new NbtString("show_text")), new("action", new NbtString("run_command")), new("value", new NbtString("x"))]);
        var component = new NbtCompound([new("text", new NbtString("label")), new("hover_event", hover)]);
        var result = NativeComponentReferences.Nbt(Root, component, MinecraftProfiles.ForDataVersion(4325)!);
        Require(result.Completeness == ReferenceCompleteness.Unresolved && result.Diagnostics.Any(item => item.Code == "AmbiguousComponent"), "duplicate event fields retain the text extractor's structured diagnosis instead of throwing during the callback");
    }
    public static void MalformedEventShapesCannotClaimCoverage()
    {
        foreach (var version in new[] { 3953, 4325, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            var result = version == 3953
                ? NativeComponentReferences.EmbeddedJson(Root, "{\"text\":\"x\",\"hoverEvent\":\"unknown\"}", profile)
                : NativeComponentReferences.Nbt(Root, new NbtCompound([new("text", new NbtString("x")), new("hover_event", new NbtString("unknown"))]), profile);
            Require(result.Completeness == ReferenceCompleteness.Unresolved, "a present but unrecognized event shape cannot silently look reference-complete");
        }
    }
}
