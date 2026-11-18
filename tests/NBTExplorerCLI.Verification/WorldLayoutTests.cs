using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldLayoutTests
{
    public static void LiteralDimensionAndPackAddresses()
    {
        var directoryLayout = MinecraftProfiles.ForDataVersion(3953)!; var modern = MinecraftProfiles.ForDataVersion(4774)!;
        var directoryDimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [""] = "minecraft:overworld", ["DIM-1"] = "minecraft:the_nether", ["DIM1"] = "minecraft:the_end", ["dimensions/test/rooms/inside"] = "test:rooms/inside"
        };
        var modernDimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dimensions/minecraft/overworld"] = "minecraft:overworld", ["dimensions/minecraft/the_nether"] = "minecraft:the_nether", ["dimensions/test/rooms/inside"] = "test:rooms/inside"
        };
        var packs = new Dictionary<string, PackSourceKind>(StringComparer.Ordinal)
        {
            ["datapacks/p"] = PackSourceKind.Datapack, ["resourcepacks/nested/a"] = PackSourceKind.ResourcePack
        };
        foreach (var row in new[] { ("region/r.-1.0.mca", "minecraft:overworld"), ("DIM-1/region/r.-1.0.mca", "minecraft:the_nether"), ("dimensions/test/rooms/inside/region/r.-1.0.mca", "test:rooms/inside") })
        {
            var found = WorldLayoutLocator.Locate(World, row.Item1, directoryLayout, directoryDimensions, packs);
            Require(found.Role == WorldSourceRole.Terrain && found.Dimension == row.Item2 && found.RegionX == -1 && found.RegionZ == 0
                && found.Artifact.RelativePath == row.Item1 && found.Artifact.Kind == ArtifactKind.Region, "literal negative region path and declared dimension stay exact");
        }
        var entity = WorldLayoutLocator.Locate(World, "dimensions/minecraft/the_nether/entities/r.0.-2.mca", modern, modernDimensions, packs);
        Require(entity.Role == WorldSourceRole.Entities && entity.Artifact.Kind == ArtifactKind.Entities && entity.Dimension == "minecraft:the_nether" && entity.RegionZ == -2, "modern entity location retains full dimension root");
        var data = WorldLayoutLocator.Locate(World, "data/minecraft/bossbars/example.json", modern, modernDimensions, packs);
        Require(data.Role == WorldSourceRole.SavedData && data.Artifact.RelativePath == "data/minecraft/bossbars/example.json", "namespaced data path is never prepended twice");
        var function = WorldLayoutLocator.Locate(World, "datapacks/p/data/models/function/demo.mcfunction", modern, modernDimensions, packs);
        Require(function.Role == WorldSourceRole.Function && function.PackRoot == "datapacks/p", "namespace named models remains a function source");
        var language = WorldLayoutLocator.Locate(World, "resourcepacks/nested/a/assets/test/lang/en_us.json", directoryLayout, directoryDimensions, packs);
        Require(language.Role == WorldSourceRole.Language && language.PackKind == PackSourceKind.ResourcePack, "explicit nested directory pack keeps identity");
        var a = WorldLayoutLocator.Locate(World, "pack-a.zip", directoryLayout, directoryDimensions, packs);
        var b = WorldLayoutLocator.Locate(World, "pack-b.zip", directoryLayout, directoryDimensions, packs);
        Require(a.Role == WorldSourceRole.Archive && b.Role == WorldSourceRole.Archive && a.Artifact != b.Artifact, "multiple archive basenames never become resources.zip");
    }
    public static void UnknownAndInvalidAreExplicit()
    {
        var modern = MinecraftProfiles.ForDataVersion(4774)!;
        var dimensions = new Dictionary<string, string> { ["dimensions/test/path"] = "test:path" };
        var packs = new Dictionary<string, PackSourceKind>();
        var unknown = WorldLayoutLocator.Locate(World, "region/r.0.0.mca", modern, dimensions, packs);
        Require(unknown.Role == WorldSourceRole.Unknown, "unexpected layout is not silently assigned overworld");
        Reject<ArgumentException>(() => WorldLayoutLocator.Locate(World, "../escape.dat", modern, dimensions, packs));
        BinaryNbtTests.RejectCodec(CodecError.InvalidRegion, () => WorldLayoutLocator.Locate(World, "dimensions/test/path/region/r.bad.0.mca", modern, dimensions, packs));
        BinaryNbtTests.RejectCodec(CodecError.InvalidRegion, () => WorldLayoutLocator.Locate(World, "dimensions/test/path/region/r.2147483647.0.mca", modern, dimensions, packs));
        Require(WorldLayoutLocator.Locate(World, "level.dat_old", modern, dimensions, packs).Role == WorldSourceRole.Irrelevant, "explicit old backup is not a current source");
        var player = WorldLayoutLocator.Locate(World, "players/id/player.dat", modern, dimensions, packs);
        Require(player.Role == WorldSourceRole.Player, "modern player tree is declared independently of directoryLayout playerdata");
        Require(WorldLayoutLocator.Locate(World, "playerdata/id.dat", modern, dimensions, packs).Role == WorldSourceRole.Unknown, "directoryLayout player directory cannot be guessed under modern profile");
    }
}
