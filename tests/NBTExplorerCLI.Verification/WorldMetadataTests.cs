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
    public static void RegistryBackedDimensionRoots()
    {
        var directoryView = WorldMetadata.Read(Level(3953, Dimensions));
        Require(directoryView.Profile.Id == "java-1.21" && directoryView.DimensionDirectories[""] == "minecraft:overworld"
            && directoryView.DimensionDirectories["DIM-1"] == "minecraft:the_nether" && directoryView.DimensionDirectories["DIM1"] == "minecraft:the_end"
            && directoryView.DimensionDirectories["dimensions/models/tags/worldgen"] == "models:tags/worldgen", "directoryView registry retains built-in and literal custom namespace/path roots");
        var modern = WorldMetadata.Read(Level(4774), Separate(4774, Dimensions));
        Require(modern.WorldGenDataVersion == 4774 && modern.DimensionDirectories["dimensions/minecraft/overworld"] == "minecraft:overworld"
            && !modern.DimensionDirectories.ContainsKey("") && modern.DimensionDirectories.Count == 4, "modern layout requires its separately versioned registry");
        var mixed = WorldMetadata.Read(Level(4774), Separate(4325, Dimensions));
        Require(mixed.DataVersion == 4774 && mixed.WorldGenDataVersion == 4325, "each artifact keeps its own known version evidence");
    }
    public static void MissingConflictingAndUnsafeEvidence()
    {
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
