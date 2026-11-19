using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class PackTextExtractionTests
{
    private static PackMetadata Metadata(int format, PackSourceKind kind = PackSourceKind.Datapack)
        => new(kind, format, new(format, 0), new(format, int.MaxValue));
    public static void IndependentPackAndItemTagProfiles()
    {
        foreach (var format in new[] { 4, 6, 10, 12, 13, 15 })
        {
            var path = "datapacks/example/data/example/advancements/start.json";
            var source = new WorldSourceLocation(new(World, path, ArtifactKind.Json), WorldSourceRole.PackJson,
                PackRoot: "datapacks/example", PackKind: PackSourceKind.Datapack);
            var result = PackTextExtractor.Extract(source, "{\"display\":{\"title\":{\"text\":\"Older pack\"}}}"u8.ToArray(), Metadata(format));
            Require(result.Complete && result.Occurrences.Single().Text == "Older pack",
                $"historical datapack format {format} retains plural directories and native JSON text");
        }
        Require(MinecraftProfiles.ForPackMetadata(Metadata(3)) is null,
            "metadata predating Java datapacks must not inherit a datapack grammar");
        var olderPack = MinecraftProfiles.ForPackMetadata(Metadata(6))!;
        var function = new SourceAddress(new(World, "datapacks/example/data/example/functions/start.mcfunction", ArtifactKind.McFunction),
            TypedPath.Root, "command.function");
        var sign = CommandTextExtractor.Extract(new(function, """setblock ~ ~ ~ oak_sign{Text1:'{"text":"Older sign"}',FilteredText1:'{"text":"Filtered sign"}'}"""), olderPack);
        Require(sign.Complete && sign.Text.Occurrences.Select(row => row.Text).SequenceEqual(new[] { "Older sign", "Filtered sign" }),
            "pre-1.20 signed JSON lines use their original SNBT string spans");
        var mixedSign = CommandTextExtractor.Extract(new(function, """setblock ~ ~ ~ oak_sign{Text1:'{"text":"Older sign"}',front_text:{messages:[]}}"""), olderPack);
        Require(!mixedSign.Complete, "conflicting single-sided and two-sided sign fields cannot claim complete coverage");
        var old = MinecraftProfiles.ForDataVersion(3700);
        Require(old is not null && old.Id == "java-1.20.4" && old.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4"
            && old.Features[ProfileFacet.PackLayout].Schema == "java-pack-plural" && old.Lists.Encoding == LogicalListEncoding.Homogeneous, "official 1.20.4 DV3700 has pre-components items and plural pack directories");
        Require(MinecraftProfiles.ForDataVersion(3465) is { Id: "java-1.20.1" } early
            && MinecraftProfiles.ForId(early.Id) == early && MinecraftProfiles.RequiredResourcePackFormat(3465).Major == 15,
            "Java 1.20.1 binds DataVersion 3465 and resource format 15");
        foreach (var (kind, format, expected) in new[] { (PackSourceKind.Datapack, 15, "java-1.20.1"), (PackSourceKind.ResourcePack, 15, "java-1.20.1"),
            (PackSourceKind.Datapack, 26, "java-1.20.4"), (PackSourceKind.ResourcePack, 22, "java-1.20.4"),
            (PackSourceKind.Datapack, 18, "java-1.20.1"), (PackSourceKind.ResourcePack, 18, "java-1.20.1"),
            (PackSourceKind.Datapack, 41, "java-1.20.5"), (PackSourceKind.ResourcePack, 32, "java-1.20.5"),
            (PackSourceKind.Datapack, 45, "java-1.21"), (PackSourceKind.ResourcePack, 46, "java-1.21"),
            (PackSourceKind.Datapack, 48, "java-1.21"), (PackSourceKind.ResourcePack, 34, "java-1.21"),
            (PackSourceKind.Datapack, 71, "java-1.21.5"), (PackSourceKind.ResourcePack, 55, "java-1.21.5") })
            Require(MinecraftProfiles.ForPackMetadata(Metadata(format, kind))?.Id == expected, "pack schema comes from its declared format family");
        Require(MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, null, new(99, 0), new(99, int.MaxValue)))?.Id == "java-26.1-snapshot-6"
            && MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, null, new(99, 1), new(99, 2)))?.Id == "java-26.1-snapshot-6"
            && MinecraftProfiles.ForPackMetadata(Metadata(70))?.Id == "java-1.21.5", "known adjacent pack formats share one profile");
        var earlyPack = MinecraftProfiles.ForPackMetadata(Metadata(62));
        Require(earlyPack == MinecraftProfiles.ForDataVersion(4298) && MinecraftProfiles.ForPackMetadata(Metadata(63)) == MinecraftProfiles.ForDataVersion(4304)
            && earlyPack?.Lists.Encoding == LogicalListEncoding.CompoundWrapper && MinecraftProfiles.SnbtGrammar(earlyPack) == SnbtProfile.JavaThrough1214,
            "the first native-NBT pack formats share binary wrappers and the earlier command grammar");
        Require(MinecraftProfiles.ForPackMetadata(Metadata(64)) == MinecraftProfiles.ForDataVersion(4308)
            && MinecraftProfiles.ForPackMetadata(Metadata(68)) == MinecraftProfiles.ForDataVersion(4316)
            && MinecraftProfiles.ForPackMetadata(Metadata(53, PackSourceKind.ResourcePack)) == MinecraftProfiles.ForDataVersion(4316),
            "data and resource pack formats share the grammar when their SNBT features agree");
        foreach (var (format, equipment) in new[] { (62, "\"ArmorItems\":[ITEM]"), (63, "\"equipment\":{\"head\":ITEM}") })
        {
            const string item = """{"id":"minecraft:paper","count":1,"components":{"minecraft:custom_name":{"text":"Held note"}}}""";
            var recipe = """{"type":"minecraft:crafting_shapeless","ingredients":["minecraft:paper"],"result":{"id":"minecraft:armor_stand","components":{"minecraft:entity_data":{"id":"minecraft:armor_stand",""" + equipment.Replace("ITEM", item, StringComparison.Ordinal) + "}}}}";
            const string pack = "datapacks/example";
            var source = new WorldSourceLocation(new(World, pack + "/data/example/recipe/equipment.json", ArtifactKind.Json), WorldSourceRole.PackJson,
                PackRoot: pack, PackKind: PackSourceKind.Datapack);
            var extracted = PackTextExtractor.Extract(source, Encoding.UTF8.GetBytes(recipe), Metadata(format));
            Require(extracted.Complete && extracted.Occurrences.SingleOrDefault()?.Text == "Held note", "pack entity equipment matches the original schema");
            var loot = source with { Artifact = new(World, pack + "/data/example/loot_table/name.json", ArtifactKind.Json) };
            var table = """{"pools":[{"rolls":1,"entries":[{"type":"minecraft:item","name":"minecraft:paper","functions":[{"function":"minecraft:set_name","name":{"text":"Loot name"}}]}]}]}""";
            var lootResult = PackTextExtractor.Extract(loot, Encoding.UTF8.GetBytes(table), Metadata(format));
            Require(lootResult.Complete && lootResult.Occurrences.SingleOrDefault()?.Text == "Loot name", "native text loot function remains recognized before the equipment change");
        }
        foreach (var (kind, major, minor) in new[] {
            (PackSourceKind.Datapack, 101, 1), (PackSourceKind.Datapack, 107, 1), (PackSourceKind.Datapack, 121, 0), (PackSourceKind.Datapack, 128, 0),
            (PackSourceKind.ResourcePack, 84, 0), (PackSourceKind.ResourcePack, 88, 0), (PackSourceKind.ResourcePack, 97, 1), (PackSourceKind.ResourcePack, 104, 0)
        })
        {
            var format = new PackFormatVersion(major, minor);
            Require(MinecraftProfiles.ForPackMetadata(new(kind, null, format, format))?.Id == "java-26.1-snapshot-6",
                $"the namespaced world era reads verified pack format {kind} {major}.{minor}");
        }
        foreach (var (kind, major, role, path, json) in new[]
        {
            (PackSourceKind.Datapack, 129, WorldSourceRole.PackJson, "data/test/advancement/example.json", "{\"display\":{\"title\":\"Example\"}}"),
            (PackSourceKind.ResourcePack, 105, WorldSourceRole.Language, "assets/test/lang/en_us.json", "{\"test.example\":\"Example\"}")
        })
        {
            var metadata = PackMetadata.Read(Encoding.UTF8.GetBytes($$$"""{"pack":{"min_format":[{{{major}}},1],"max_format":[{{{major}}},2],"description":"Example"}}"""), kind);
            Require(MinecraftProfiles.ForPackMetadata(metadata) == MinecraftProfiles.ForDataVersion(5130),
                "a new format number with the same metadata structure reuses the current schema");
            var source = new WorldSourceLocation(new(World, "pack/" + path, ArtifactKind.Json), role, PackRoot: "pack", PackKind: kind);
            var text = PackTextExtractor.Extract(source, Encoding.UTF8.GetBytes(json), metadata);
            Require(text.Complete && text.Occurrences.Single().Text == "Example", "same-schema pack contents remain readable with a new format number");
        }
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => MinecraftProfiles.RequiredResourcePackFormat(5131));
        foreach (var entry in new[] { "assets/p/models/item/display.json", "assets/p/font/icons.json", "assets/p/shaders/core/demo.json" })
        {
            var source = new WorldSourceLocation(new(World, "resources.zip", ArtifactKind.Archive, entry),
                WorldSourceRole.PackJson, PackRoot: "resources.zip", PackKind: PackSourceKind.ResourcePack);
            var text = PackTextExtractor.Extract(source, Encoding.UTF8.GetBytes("{}"), Metadata(15, PackSourceKind.ResourcePack));
            Require(text.Complete && text.Occurrences.IsEmpty, "native resource definitions do not authorize translation slots: " + entry);
        }
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

        var predicate = new WorldSourceLocation(new(World, "datapacks/p/data/test/predicates/gate.json", ArtifactKind.Json),
            WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
        const string staticCondition = """{"condition":"minecraft:entity_properties","entity":"this","predicate":{"nbt":"{Tags:[\"checkpoint\"]}","equipment":{"mainhand":{"items":["minecraft:stick"],"nbt":"{Progress:2b}"}}}}""";
        var nested = "{\"condition\":\"minecraft:all_of\",\"terms\":[" + staticCondition + "]}";
        TextExtractionResult ReadCondition(string value) => PackTextExtractor.Extract(predicate, Encoding.UTF8.GetBytes(value), Metadata(15));
        Require(ReadCondition(nested) is { Complete: true, Occurrences.IsEmpty: true }, "a vanilla item and entity predicate is recognized without creating editable text");
        const string itemCondition = """{"condition":"minecraft:entity_properties","entity":"this","predicate":{"nbt":"{SelectedItem:{id:'minecraft:paper',tag:{Enchantments:[{id:'minecraft:unbreaking',lvl:2s}]}}}"}}""";
        Require(ReadCondition(itemCondition) is { Complete: true, Occurrences.IsEmpty: true },
            "entity predicate item and enchantment IDs stay structural");
        const string nameCondition = """{"condition":"minecraft:entity_properties","entity":"this","predicate":{"nbt":"{CustomName:'{\"text\":\"Guard\"}'}"}}""";
        Require(!ReadCondition(nameCondition).Complete
            && !ReadCondition(nested.Replace("minecraft:entity_properties", "test:unknown", StringComparison.Ordinal)).Complete,
            "name-bearing NBT and unknown predicate dispatch remain unresolved");
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
        WorldgenSchemasProtectData();
        var source = new WorldSourceLocation(new(World, "datapacks/p/data/models/advancement/a.json", ArtifactKind.Json), WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
        var bytes = Encoding.UTF8.GetBytes("{\"display\":{\"title\":{\"unknown\":\"Excluded\"},\"description\":{\"text\":\"Selected\"}}}");
        var scope = new SnapshotScope([], [], ["advancement.description"], []);
        var result = PackTextExtractor.Extract(source, bytes, Metadata(48), scope: scope);
        Require(result.Complete && result.Occurrences.Single().Text == "Selected" && result.Occurrences.Single().Address.SemanticRole == "advancement.description", "pack type filter reaches selected component before schema parsing");
    }
    private static void WorldgenSchemasProtectData()
    {
        foreach (var (kind, valid, unknown) in new[]
        {
            ("dimension", """{"type":"minecraft:overworld","generator":{"type":"minecraft:flat","settings":{"biome":"minecraft:plains","layers":[{"block":"minecraft:bedrock","height":1}],"lakes":false,"features":false}}}""",
             """{"type":"minecraft:overworld","generator":{"type":"test:generator","text":"Unknown payload"}}"""),
            ("dimension_type", """{"ultrawarm":false,"natural":true,"has_skylight":true,"effects":"minecraft:overworld","infiniburn":"#minecraft:infiniburn_overworld","min_y":-64,"height":384,"logical_height":384}""",
             """{"ultrawarm":"not a flag","height":384,"effects":{"text":"Unknown payload"}}"""),
            ("worldgen/biome", """{"temperature":0.5,"downfall":0.4,"has_precipitation":true,"effects":{"fog_color":0,"water_color":1,"water_fog_color":2,"sky_color":3,"music":{"sound":"","min_delay":1,"max_delay":2,"replace_current_music":true},"particle":{"options":{"type":"minecraft:snowflake"},"probability":0.1}},"carvers":{"air":[]},"features":[],"spawners":{},"spawn_costs":{}}""",
             """{"effects":{"fog_color":0,"water_color":1,"water_fog_color":2,"sky_color":3,"particle":{"options":{"type":"test:particle","text":"Unknown payload"},"probability":0.1}}}""")
        })
        {
            var source = new WorldSourceLocation(new(World, "datapacks/p/data/test/" + kind + "/entry.json", ArtifactKind.Json),
                WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
            TextExtractionResult Read(string value) => PackTextExtractor.Extract(source, Encoding.UTF8.GetBytes(value), Metadata(15));
            Require(Read(valid) is { Complete: true, Occurrences.IsEmpty: true }, "known vanilla worldgen has no display text: " + kind);
            Require(Read(unknown) is { Complete: false, Occurrences.IsEmpty: true }, "unknown worldgen remains protected: " + kind);
        }
    }
}
