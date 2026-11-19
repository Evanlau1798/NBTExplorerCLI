using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldMetadataTests
{
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
    public static void RegistryBackedDimensionRoots()
    {
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
        var modern = WorldMetadata.Read(Level(4774), Separate(4774, Dimensions));
        Require(modern.WorldGenDataVersion == 4774 && modern.DimensionDirectories["dimensions/minecraft/overworld"] == "minecraft:overworld"
            && !modern.DimensionDirectories.ContainsKey("") && modern.DimensionDirectories.Count == 4, "modern layout requires its separately versioned registry");
        var mixed = WorldMetadata.Read(Level(4774), Separate(4325, Dimensions));
        Require(mixed.DataVersion == 4774 && mixed.WorldGenDataVersion == 4325, "each artifact keeps its own known version evidence");
    }
    public static void MissingConflictingAndUnsafeEvidence()
    {
        foreach (var marker in new NbtEntry[] { new("DataVersion", new NbtInt(9999)), new("Version", new NbtCompound([])), new("WorldGenSettings", new NbtCompound([])) })
            BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Classic(marker)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Classic(), Separate(4774, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4775, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4774)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(Level(4774), Separate(999999, Dimensions)));
        BinaryNbtTests.RejectCodec(CodecError.InvalidTag, () => WorldMetadata.Read(Level(3953, Dimensions, claimedVersion: 4774)));
        Reject<ArgumentException>(() => WorldMetadata.Read(Level(3953, new([new("test:../escape", new NbtCompound([]))]))));
        Reject<ArgumentException>(() => WorldMetadata.Read(Level(3953, new([new("test:path", new NbtCompound([])), new("test:path", new NbtCompound([]))]))));
        var series = new NbtDocument("", new NbtCompound([new("Data", new NbtCompound([
            new("DataVersion", new NbtInt(3953)), new("Version", new NbtCompound([new("Id", new NbtInt(3953)), new("Series", new NbtString("foreign-series"))])),
            new("WorldGenSettings", new NbtCompound([new("dimensions", Dimensions)]))]))]));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => WorldMetadata.Read(series));
    }
}
