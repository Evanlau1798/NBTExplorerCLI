using System.Text;
using Next.Domain;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class PackTextExtractionTests
{
    private static PackMetadata Metadata(int format, PackSourceKind kind = PackSourceKind.Datapack)
        => new(kind, format, new(format, 0), new(format, int.MaxValue));
    public static void IndependentPackAndLegacyTagProfiles()
    {
        var old = MinecraftProfiles.ForDataVersion(3700);
        Require(old is not null && old.Id == "java-1.20.4" && old.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4"
            && old.Features[ProfileFacet.PackLayout].Schema == "java-pack-plural" && old.Lists.Encoding == LogicalListEncoding.Homogeneous, "official 1.20.4 DV3700 has pre-components items and plural pack directories");
        foreach (var (kind, format, expected) in new[] { (PackSourceKind.Datapack, 26, "java-1.20.4"), (PackSourceKind.ResourcePack, 22, "java-1.20.4"),
            (PackSourceKind.Datapack, 48, "java-1.21"), (PackSourceKind.ResourcePack, 34, "java-1.21"),
            (PackSourceKind.Datapack, 71, "java-1.21.5"), (PackSourceKind.ResourcePack, 55, "java-1.21.5") })
            Require(MinecraftProfiles.ForPackMetadata(Metadata(format, kind))?.Id == expected, "pack schema comes from its own exact declared format, not world version");
        Require(MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, null, new(99, 0), new(99, int.MaxValue)))?.Id == "java-26.1-snapshot-6"
            && MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, null, new(99, 1), new(99, 2))) is null
            && MinecraftProfiles.ForPackMetadata(Metadata(70)) is null, "known format99.0 has exact snapshot6 profile; unknown major/minimum minor cannot inherit it");
    }
    public static void LanguageValuesAndDefinitionAddresses()
    {
        var location = new WorldSourceLocation(new(World, "arbitrary-B.zip", ArtifactKind.Archive, "assets/models/lang/en_us.json"), WorldSourceRole.Language,
            PackRoot: "arbitrary-B.zip", PackKind: PackSourceKind.ResourcePack);
        var result = PackTextExtractor.Extract(location, Encoding.UTF8.GetBytes("{\"item.custom.name\":\"Item\",\"block.custom.name\":\"Block\",\"\\ud800\":\"\\udfff\"}"), Metadata(34, PackSourceKind.ResourcePack));
        Require(result.Complete && result.Occurrences.Select(text => text.Address.NodePath.ToJsonPointer() + "=" + text.Text).Order(StringComparer.Ordinal).SequenceEqual(new[] {
            "/item.custom.name=Item", "/block.custom.name=Block", "/\ud800=\udfff" }.Order(StringComparer.Ordinal)), "all language values remain literal slots, including item/block prefixes and Java UTF16");
        Require(result.TranslationKeys.Length == 3 && result.TranslationKeys.All(key => key.Address.JsonTarget == JsonTargetKind.PropertyName && key.Address.SemanticRole == "language.key")
            && result.Occurrences.All(text => text.Address.Artifact == location.Artifact && text.Address.SemanticRole == "language.value"), "key definitions and values keep exact outer archive/entry and target kind");
    }
    public static void AdvancementDescriptionAndProtectedJson()
    {
        foreach (var (format, folder) in new[] { (26, "advancements"), (48, "advancement"), (71, "advancement") })
        {
            var path = "datapacks/p/data/models/" + folder + "/start.json";
            var location = new WorldSourceLocation(new(World, path, ArtifactKind.Json), WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
            var result = PackTextExtractor.Extract(location, Encoding.UTF8.GetBytes("{\"display\":{\"title\":{\"text\":\"Title\",\"color\":\"red\"},\"description\":{\"translate\":\"test.description\",\"with\":[\"Argument\"]},\"icon\":{\"id\":\"minecraft:stone\"}},\"criteria\":{\"custom\":{\"text\":\"Protected\"}},\"rewards\":{\"function\":\"test:protected\"}}"), Metadata(format));
            Require(result.Complete && result.Occurrences.Select(text => text.Address.NodePath.ToJsonPointer() + "|" + text.Address.SemanticRole + "|" + text.Text).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/display/title/text|advancement.title|Title", "/display/description/with/0|advancement.description|Argument" }.Order(StringComparer.Ordinal)), "profile-selected plural/singular advancements expose only declared display component fields");
            Require(result.TranslationKeys.Single().Key == "test.description", "advancement translation key stays protected reference");
        }
        var meta = new WorldSourceLocation(new(World, "packs/p/pack.mcmeta", ArtifactKind.Json), WorldSourceRole.PackMetadata, PackRoot: "packs/p", PackKind: PackSourceKind.ResourcePack);
        var description = PackTextExtractor.Extract(meta, Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":34,\"description\":\"Pack name\"}}"), Metadata(34, PackSourceKind.ResourcePack));
        Require(description.Complete && description.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/pack/description" && description.Occurrences.Single().Text == "Pack name", "pack description is exact native component field");
    }
    public static void UnknownNativeKeysStayProtected()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":34,\"description\":{\"text\":\"Known\",\"\\ud800\":\"Protected\"},\"\\udfff\":1},\"\\ud800\":\"Protected\"}");
        var metadata = PackMetadata.Read(bytes, PackSourceKind.ResourcePack);
        var source = new WorldSourceLocation(new(World, "packs/p/pack.mcmeta", ArtifactKind.Json), WorldSourceRole.PackMetadata, PackRoot: "packs/p", PackKind: PackSourceKind.ResourcePack);
        var result = PackTextExtractor.Extract(source, bytes, metadata);
        Require(result.Complete && result.Occurrences.Single().Text == "Known" && result.Occurrences.Single().Address.NodePath.ToJsonPointer() == "/pack/description/text", "unknown Unicode property names never disturb known fields or become authorized slots");
    }
    public static void TypesSelectActualPackFields()
    {
        var source = new WorldSourceLocation(new(World, "datapacks/p/data/models/advancement/a.json", ArtifactKind.Json), WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
        var bytes = Encoding.UTF8.GetBytes("{\"display\":{\"title\":{\"unknown\":\"Excluded\"},\"description\":{\"text\":\"Selected\"}}}");
        var scope = new SnapshotScope([], [], ["advancement.description"], []);
        var result = PackTextExtractor.Extract(source, bytes, Metadata(48), scope: scope);
        Require(result.Complete && result.Occurrences.Single().Text == "Selected" && result.Occurrences.Single().Address.SemanticRole == "advancement.description", "pack type filter reaches selected component before schema parsing");
    }
}
