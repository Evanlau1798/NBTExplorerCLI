using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldMetadataTests
{
    internal static readonly VerificationCase[] Cases =
    [
        new("world-metadata-registry-dimensions", "discovery", _ => RegistryBackedDimensionRoots()),
        new("world-metadata-early-anvil-scoreboards", "discovery", _ => EarlyAnvilVersionedScoreboards()),
        new("world-metadata-early-anvil-terrain-text", "discovery", _ => EarlyAnvilTerrainText()),
        new("world-metadata-early-anvil-independent-text-transitions", "discovery", _ => EarlyAnvilTextTransitions()),
        new("world-metadata-versioned-map-banners", "discovery", _ => VersionedMapBanners()),
        new("prepare-world-early-anvil-mixed-write-boundary", "preparation", _ => EarlyAnvilArtifactIsReadOnly()),
        new("world-metadata-missing-conflicting-unsafe", "discovery", _ => MissingConflictingAndUnsafeEvidence())
    ];

    private static void VersionedMapBanners()
    {
        foreach (var (version, native) in new[] { (2580, false), (4774, true) })
        {
            var path = native ? "dimensions/minecraft/overworld/data/minecraft/map_24.dat" : "data/map_24.dat";
            var source = new WorldSourceLocation(new(World, path, ArtifactKind.Nbt), WorldSourceRole.SavedData, "minecraft:overworld");
            var nameKey = native ? "name" : "Name";
            var banner = C(E(native ? "pos" : "Pos", native ? new NbtIntArray([2, 64, 3]) :
                C(E("X", new NbtInt(2)), E("Y", new NbtInt(64)), E("Z", new NbtInt(3)))),
                E(native ? "color" : "Color", S("red")), E(nameKey, Text("Market", native)));
            var data = C(E("dimension", S("minecraft:overworld")), E("scale", new NbtByte(0)),
                E("xCenter", new NbtInt(0)), E("zCenter", new NbtInt(0)), E("colors", new NbtByteArray([0])),
                E("banners", L(banner)), E("frames", L()));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", data)));
            var result = NbtTextExtractor.Extract(source, document);
            var name = result.Occurrences.SingleOrDefault();
            Require(result.Complete && name is { Text: "Market", Address.SemanticRole: "map.banner.name" }
                && name.Address.NodePath.ToJsonPointer() == "/data/banners/0/" + nameKey + (native ? "/text" : ""),
                "versioned map banner name uses its exact physical field and text format");
            if (!native) MapBannerWriteRollsBack(path, document);
        }
    }
    private static void MapBannerWriteRollsBack(string path, NbtDocument document)
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var original = CompressedNbt.WriteDocument(document, NbtCompression.GZip);
            WorldDiscoveryTests.Put(root, path, original);
            var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache"));
            var row = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == path);
            var slot = row.Text!.Occurrences.Single().ToSlot("en_us", "zh_tw");
            var inventory = new WorldInventory(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("map-banner", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "市集")))]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state);
            var file = Path.Combine(root, path);
            Require(engine.Apply(prepared, true).Success && NbtTextExtractor.Extract(row.Source.Location,
                CompressedNbt.ReadDocument(File.ReadAllBytes(file), NbtCompression.GZip).Document).Occurrences.Single().Text == "市集",
                "an embedded map banner name is rewritten and read through its original schema");
            var rolledBack = engine.Recover(plan.PlanHash, true, rollback: true);
            Require(rolledBack.Counts[OperationOutcome.RolledBack] == 1 && File.ReadAllBytes(file).SequenceEqual(original),
                "map banner rollback restores the exact original bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }

    internal static NbtCompound Dimensions => new([
        new("minecraft:overworld", new NbtCompound([])), new("minecraft:the_nether", new NbtCompound([])),
        new("minecraft:the_end", new NbtCompound([])), new("models:tags/worldgen", new NbtCompound([]))]);
    internal static NbtDocument Level(int version, NbtCompound? dimensions = null, int? claimedVersion = null)
    {
        var data = new List<NbtEntry> { new("DataVersion", new NbtInt(version)), new("Version", new NbtCompound([
            new("Id", new NbtInt(claimedVersion ?? version)), new("Name", new NbtString("misleading directory label"))])) };
        if (dimensions is not null) data.Add(new("WorldGenSettings", new NbtCompound([new("dimensions", dimensions)])));
        return new("", new NbtCompound([new("Data", new NbtCompound(data))]));
    }
    internal static NbtDocument Separate(int version, NbtCompound dimensions) => new("", new NbtCompound([
        new("DataVersion", new NbtInt(version)), new("data", new NbtCompound([new("dimensions", dimensions)]))]));
    internal static NbtDocument Classic(params NbtEntry[] extra) => new("", new NbtCompound([
        new("Data", new NbtCompound([new("version", new NbtInt(19133)), new("RandomSeed", new NbtLong(17)),
            new("SpawnX", new NbtInt(0)), new("SpawnZ", new NbtInt(0)), .. extra]))]));
    public static void EarlyAnvilVersionedScoreboards()
    {
        var artifact = new ArtifactAddress(Guid.NewGuid(), "data/scoreboard.dat", ArtifactKind.Nbt);
        var source = new WorldSourceLocation(artifact, WorldSourceRole.SavedData);
        const string encoded = "{\"text\":\"Name\"}";
        foreach (var (version, ownVersion, visible) in new[]
        {
            (100, false, encoded), (169, false, encoded), (1343, false, encoded), (1493, true, encoded), (1513, true, encoded),
            (1514, true, "Name"), (1516, true, "Name"), (1519, true, "Name"), (1631, true, "Name"), (1975, true, "Name")
        })
        {
            var world = WorldMetadata.Read(Classic(new NbtEntry("DataVersion", new NbtInt(version))));
            Require(world.DataVersion == version && world.DimensionDirectories["DIM1"] == "minecraft:the_end", "early DataVersion keeps the original Anvil paths");
            var objective = new NbtCompound([new("DisplayName", new NbtString(encoded))]);
            var data = new NbtCompound([new("Objectives", new NbtList(NbtType.Compound, [objective]))]);
            var entries = new List<NbtEntry> { new("data", data) };
            if (ownVersion) entries.Insert(0, new("DataVersion", new NbtInt(version)));
            var board = new NbtDocument("", new NbtCompound(entries));
            var result = NbtTextExtractor.Extract(source, board, layoutProfile: world.Profile);
            Require(result.Complete && result.Occurrences.Single().Text == visible, "old Anvil scoreboard schemas distinguish literals from JSON components by version evidence");
        }
        Require(Enumerable.Range(100, 1877).All(version => MinecraftProfiles.ForDataVersion(version) is not null), "versioned Anvil coverage has no numeric gaps before Java 1.14.4");

        var village = source with { Artifact = new(artifact.WorldId, "data/villages_nether.dat", ArtifactKind.Nbt) };
        NbtDocument Villages(NbtValue list) => new("", new NbtCompound([new("DataVersion", new NbtInt(1631)),
            new("data", new NbtCompound([new("Villages", list), new("Tick", new NbtInt(12))]))]));
        Require(NbtTextExtractor.Extract(village, Villages(new NbtList(NbtType.End, []))) is { Complete: true, Occurrences.IsEmpty: true },
            "versioned village state is a known nontext artifact");
        Require(!NbtTextExtractor.Extract(village, Villages(new NbtString("unexpected"))).Complete,
            "an incorrect village list cannot claim nontext coverage");
    }
    public static void RegistryBackedDimensionRoots()
    {
        foreach (var version in new[] { 1976, 2565 })
        {
            var old = WorldMetadata.Read(Classic(new NbtEntry("DataVersion", new NbtInt(version))));
            Require(old.DataVersion == version && old.DimensionDirectories.Count == 3 &&
                old.DimensionDirectories["DIM-1"] == "minecraft:the_nether", "pre-worldgen worlds use the game's three built-in dimensions");
        }
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(1976)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Classic(new NbtEntry("DataVersion", new NbtInt(2566)))));
        var directoryView = WorldMetadata.Read(Level(3953, Dimensions));
        Require(directoryView.Profile.Id == "java-1.21" && directoryView.DimensionDirectories[""] == "minecraft:overworld"
            && directoryView.DimensionDirectories["DIM-1"] == "minecraft:the_nether" && directoryView.DimensionDirectories["DIM1"] == "minecraft:the_end"
            && directoryView.DimensionDirectories["dimensions/models/tags/worldgen"] == "models:tags/worldgen", "directoryView registry retains built-in and literal custom namespace/path roots");
        foreach (var (version, itemSchema, layout) in new[] {
            (3463, "java-item-tags-1.20.4", "java-pack-plural"),
            (3578, "java-item-tags-1.20.4", "java-pack-plural"),
            (3818, "java-item-tags-1.20.4", "java-pack-plural"),
            (3819, "java-item-components-1.20.5", "java-pack-plural"),
            (3839, "java-item-components-1.20.5", "java-pack-plural"),
            (3945, "java-item-components-1.20.5", "java-pack-plural"),
            (3946, "java-item-components-1.20.5", "java-pack-singular"),
            (3955, "java-item-components-1.20.5", "java-pack-singular"),
            (4189, "java-item-components-1.20.5", "java-pack-singular")
        })
        {
            var intermediate = WorldMetadata.Read(Level(version, Dimensions));
            Require(intermediate.DataVersion == version && MinecraftProfiles.ForId(intermediate.Profile.Id) == intermediate.Profile
                && intermediate.Profile.Features[ProfileFacet.ItemSchema].Schema == itemSchema
                && intermediate.Profile.Features[ProfileFacet.PackLayout].Schema == layout, $"Java DataVersion {version} resolves by its real storage family");
        }
        foreach (var (version, expected) in new[] { (3578, 18), (3839, 32), (3955, 34), (4189, 46) })
            Require(MinecraftProfiles.RequiredResourcePackFormat(version).Major == expected, "generated metadata uses the game's actual resource format");
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => MinecraftProfiles.RequiredResourcePackFormat(3818));
        foreach (var (version, grammar, textVersion) in new[] {
            (4298, SnbtProfile.JavaThrough1214, 1), (4304, SnbtProfile.JavaThrough1214, 1), (4307, SnbtProfile.JavaThrough1214, 1),
            (4308, SnbtProfile.JavaMixedLists, 1), (4316, SnbtProfile.JavaMixedLists, 1),
            (4317, SnbtProfile.Java1215, 1), (4535, SnbtProfile.Java1215, 1),
            (4536, SnbtProfile.Java1215, 2), (4554, SnbtProfile.Java1215, 2), (4773, SnbtProfile.Java1215, 2)
        })
        {
            var native = WorldMetadata.Read(Level(version, Dimensions)).Profile;
            Require(native.Features[ProfileFacet.TextSchema] is { Schema: "java-text-nbt", Version: var actual }
                && actual == textVersion && native.Lists.Encoding == LogicalListEncoding.CompoundWrapper
                && MinecraftProfiles.SnbtGrammar(native) == grammar, $"DataVersion {version} selects independent binary text and SNBT formats");
        }
        var modern = WorldMetadata.Read(Level(4774), Separate(4774, Dimensions));
        Require(modern.WorldGenDataVersion == 4774 && modern.DimensionDirectories["dimensions/minecraft/overworld"] == "minecraft:overworld"
            && !modern.DimensionDirectories.ContainsKey("") && modern.DimensionDirectories.Count == 4, "modern layout requires its separately versioned registry");
        var mixed = WorldMetadata.Read(Level(4774), Separate(4325, Dimensions));
        Require(mixed.DataVersion == 4774 && mixed.WorldGenDataVersion == 4325, "each artifact keeps its own known version evidence");
        foreach (var version in new[] { 4775, 4786, 4903, 5023, 5130, 5131 })
        {
            var latest = WorldMetadata.Read(Level(version), Separate(version, Dimensions));
            Require(latest.Profile == modern.Profile && latest.DataVersion == version && latest.WorldGenDataVersion == version
                && latest.DimensionDirectories.SequenceEqual(modern.DimensionDirectories),
                $"DataVersion {version} can use the same independently validated namespaced layout");
        }
    }
    private static void EarlyAnvilTerrainText()
    {
        var sign = C(E("id", S("Sign")), E("Text1", Text("Guide sign", false)));
        var bookTag = C(E("title", S("Guide title")), E("pages", L(Text("Guide page", false))));
        var book = C(E("id", S("minecraft:written_book")), E("tag", bookTag));
        var display = C(E("Name", S("Plain item")), E("Lore", L(S("Plain lore"))));
        var paper = C(E("id", S("minecraft:paper")), E("tag", C(E("display", display))));
        var chest = C(E("id", S("Chest")), E("CustomName", S("Plain chest")), E("Items", L(book, paper)));
        var entity = C(E("id", S("ArmorStand")), E("CustomName", S("Plain entity")));
        var level = C(E("xPos", new NbtInt(0)), E("zPos", new NbtInt(0)), E("TileEntities", L(sign, chest)), E("Entities", L(entity)));
        var document = new NbtDocument("", C(E("DataVersion", new NbtInt(169)), E("Level", level)));
        var source = new WorldSourceLocation(new(Guid.NewGuid(), "region/r.0.0.mca", ArtifactKind.Region), WorldSourceRole.Terrain, "minecraft:overworld");
        var result = NbtTextExtractor.Extract(source, document, new(0, 0));
        Require(result.Complete && result.Occurrences.Select(item => item.Text).Order(StringComparer.Ordinal).SequenceEqual(
            new[] { "Guide page", "Guide sign", "Guide title", "Plain chest", "Plain entity", "Plain item", "Plain lore" }),
            "early Anvil separates plain metadata from JSON book pages and sign lines");

        var commandBlock = C(E("id", S("Control")), E("Command", S("tellraw @a {text:Guide}")));
        var commandsLevel = C(E("xPos", new NbtInt(0)), E("zPos", new NbtInt(0)), E("TileEntities", L(commandBlock)));
        var commands = NbtTextExtractor.Extract(source, new("", C(E("DataVersion", new NbtInt(169)), E("Level", commandsLevel))), new(0, 0));
        Require(!commands.Complete && commands.Occurrences.Single().Address.SemanticRole == "command.block.raw" && commands.PendingCommands.IsEmpty,
            "early command blocks expose raw text without claiming modern command semantics");
    }
    private static void EarlyAnvilArtifactIsReadOnly()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string path = "playerdata/early.dat";
            var display = C(E("Name", S("Plain item")));
            var paper = C(E("id", S("minecraft:paper")), E("tag", C(E("display", display))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(169)), E("Inventory", L(paper))));
            var before = CompressedNbt.WriteDocument(document, NbtCompression.GZip);
            WorldDiscoveryTests.Put(root, path, before);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path).ToSlot("en_us", "zh_tw");
            var operation = new LiteralPatch("early-item", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "物品")));
            var inventory = new WorldInventory(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () =>
            {
                using var candidate = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            });
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(before), "the versioned artifact remains byte-identical");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static void EarlyAnvilTextTransitions()
    {
        var source = new WorldSourceLocation(new(Guid.NewGuid(), "playerdata/example.dat", ArtifactKind.Nbt), WorldSourceRole.Player);
        foreach (var (version, jsonName, jsonLore) in new[] { (1457, false, false), (1458, true, false), (1513, true, false),
            (1514, true, false), (1631, true, false), (1802, true, false), (1803, true, true) })
        {
            var display = C(E("Name", jsonName ? Text("Item name", false) : S("Item name")),
                E("Lore", L(jsonLore ? Text("Item lore", false) : S("Item lore"))));
            var item = C(E("id", S("minecraft:paper")), E("tag", C(E("display", display))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("Inventory", L(item))));
            var actual = NbtTextExtractor.Extract(source, document);
            Require(actual.Complete && actual.Occurrences.Select(occurrence => occurrence.Text).SequenceEqual(new[] { "Item name", "Item lore" }),
                $"DataVersion {version} uses independent item name and lore formats");
        }
    }
    public static void MissingConflictingAndUnsafeEvidence()
    {
        foreach (var marker in new NbtEntry[] { new("DataVersion", new NbtInt(9999)), new("Version", new NbtCompound([])), new("WorldGenSettings", new NbtCompound([])) })
            BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Classic(marker)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Classic(), Separate(4774, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4775, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4774)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4774), Separate(999999, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4774), Separate(169, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(5130, Dimensions), Separate(5130, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.InvalidTag, () => WorldMetadata.Read(Level(3953, Dimensions, claimedVersion: 4774)));
        Reject<ArgumentException>(() => WorldMetadata.Read(Level(3953, new([new("test:../escape", new NbtCompound([]))]))));
        Reject<ArgumentException>(() => WorldMetadata.Read(Level(3953, new([new("test:path", new NbtCompound([])), new("test:path", new NbtCompound([]))]))));
        var series = new NbtDocument("", new NbtCompound([new("Data", new NbtCompound([
            new("DataVersion", new NbtInt(3953)), new("Version", new NbtCompound([new("Id", new NbtInt(3953)), new("Series", new NbtString("foreign-series"))])),
            new("WorldGenSettings", new NbtCompound([new("dimensions", Dimensions)]))]))]));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(series));
    }
}
