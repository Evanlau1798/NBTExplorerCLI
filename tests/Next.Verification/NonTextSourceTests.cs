using Next.Domain;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.NbtItemTextTests;

namespace Next.Verification;

internal static class NonTextSourceTests
{
    public static void KnownSavedDataAndPoiRemainNontext()
    {
        foreach (var (version, path) in new[] { (3953, "data/raids.dat"), (4774, "data/minecraft/weather.dat") })
        {
            var source = new WorldSourceLocation(new(World, path, ArtifactKind.Nbt), WorldSourceRole.SavedData);
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", C(E("type", S("minecraft:protected_id"))))));
            var result = NbtTextExtractor.Extract(source, document);
            Require(result.Complete && result.Occurrences.IsEmpty, "known no-text saved-data schema is irrelevant, not unsupported or generically translated");
            if (version == 4774)
            {
                var unknown = source with { Artifact = new(World, "data/mod/weather.dat", ArtifactKind.Nbt) };
                Require(!NbtTextExtractor.Extract(unknown, document).Complete, "custom namespace cannot impersonate a vanilla nontext system by basename");
            }
        }
        var poi = new WorldSourceLocation(new(World, "poi/r.0.0.mca", ArtifactKind.Region), WorldSourceRole.PointOfInterest, "minecraft:overworld", 0, 0);
        var payload = new NbtDocument("", C(E("DataVersion", new NbtInt(4774)), E("Sections", C(E("0", C(E("Records", L(C(E("type", S("minecraft:home")))))))))));
        var points = NbtTextExtractor.Extract(poi, payload, new(0, 0));
        Require(points.Complete && points.Occurrences.IsEmpty, "validated POI records contain protected identifiers, not literal text slots");
    }
}
