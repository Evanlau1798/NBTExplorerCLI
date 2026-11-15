using Next.Domain;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class WorldSourceScopeTests
{
    public static void ScopeReachesActualSources()
    {
        var scope = new SnapshotScope(["minecraft:the_nether"], ["r.1.0.mca"], ["sign.front.line"], []);
        var selected = new WorldSourceLocation(new(World, "DIM-1/region/r.1.0.mca", ArtifactKind.Region), WorldSourceRole.Terrain, "minecraft:the_nether", 1, 0);
        var otherRegion = selected with { Artifact = new(World, "DIM-1/region/r.0.0.mca", ArtifactKind.Region), RegionX = 0 };
        var otherDimension = selected with { Artifact = new(World, "region/r.1.0.mca", ArtifactKind.Region), Dimension = "minecraft:overworld" };
        Require(WorldSourceScope.Includes(selected, scope) && !WorldSourceScope.Includes(otherRegion, scope) && !WorldSourceScope.Includes(otherDimension, scope), "dimension/region scope determines actual source set");
        var global = new WorldSourceLocation(new(World, "data/corrupt.dat", ArtifactKind.Nbt), WorldSourceRole.SavedData);
        Require(WorldSourceScope.Includes(global, scope), "global sources stay explicit under region filtering; corruption cannot become zero-items success");
        var exact = new SnapshotScope([], [], [], ["DIM-1/region/r.1.0.mca"]);
        Require(WorldSourceScope.Includes(selected, exact) && !WorldSourceScope.Includes(global, exact), "explicit physical-artifact filter stays exact");
        var archive = new WorldSourceLocation(new(World, "pack-a.zip", ArtifactKind.Archive), WorldSourceRole.Archive);
        Require(WorldSourceScope.Includes(archive, scope), "entry/content role filtering is not guessed before archive inspection");
        Require(scope.Types.SequenceEqual(new[] { "sign.front.line" }), "content-role filter is preserved for the extraction stage");
    }
}
