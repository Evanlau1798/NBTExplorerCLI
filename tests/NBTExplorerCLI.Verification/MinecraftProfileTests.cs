using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using System.Text;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class MinecraftProfileTests
{
    public static void Java12111ExactWorldAndPackEvidence()
    {
        var profile = MinecraftProfiles.ForDataVersion(4671);
        Require(profile?.Id == "java-1.21.11" && profile.Features[ProfileFacet.Layout].Schema == "java-dimensions-directory"
            && profile.Features[ProfileFacet.TextSchema].Version == 2 && profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper,
            "1.21.11 combines directory-layout worlds with the newer NBT component schema");
        Require(MinecraftProfiles.ForId("java-1.21.11") == profile && MinecraftProfiles.ForDataVersion(4670) is null
            && MinecraftProfiles.ForDataVersion(4672) is null, "only the release DataVersion selects this profile");
        Require(WorldMetadata.Read(WorldMetadataTests.Level(4671, WorldMetadataTests.Dimensions)).DimensionDirectories["DIM-1"] == "minecraft:the_nether",
            "1.21.11 resolves the world registry from level.dat and keeps separate vanilla dimension directories");
        PackMetadata Pack(int major, int minor, PackSourceKind kind) => PackMetadata.Read(Encoding.UTF8.GetBytes(
            $$$"""{"pack":{"description":"Hello","min_format":[{{{major}}},{{{minor}}}],"max_format":[{{{major}}},{{{minor}}}]}}"""), kind);
        Require(MinecraftProfiles.ForPackMetadata(Pack(94, 1, PackSourceKind.Datapack)) == profile
            && MinecraftProfiles.ForPackMetadata(Pack(94, 0, PackSourceKind.Datapack)) is null
            && MinecraftProfiles.ForPackMetadata(Pack(94, 2, PackSourceKind.Datapack)) is null
            && MinecraftProfiles.ForPackMetadata(Pack(75, 0, PackSourceKind.ResourcePack)) == profile
            && MinecraftProfiles.RequiredResourcePackFormat(profile!) == new PackFormatVersion(75, 0), "select exact major and minor versions independently");
        var command = new SourceAddress(new(Guid.NewGuid(), "text.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var text = CommandTextExtractor.Extract(new(command, "tellraw @s {text:'Hello'}"), profile!);
        Require(text.Complete && text.Text.Occurrences.Single().Text == "Hello", "the exact profile enables the known command component encoding");
        Require(LootFunctionProfiles.IsKnown(profile!, "minecraft:discard")
            && !LootFunctionProfiles.IsKnown(MinecraftProfiles.ForDataVersion(4325)!, "minecraft:discard")
            && !LootFunctionProfiles.IsKnown(profile!, "custom:discard"), "discard belongs to the pinned 1.21.11 registry");
    }

    public static void Java12111BorderAndStopwatchSavedData()
    {
        foreach (var (file, dimension, field) in new[] {
            ("DIM-1/data/world_border.dat", "minecraft:the_nether", new NbtEntry("size", NbtDouble.FromValue(1000))),
            ("data/stopwatches.dat", "minecraft:overworld", new NbtEntry("stopwatches", new NbtCompound([])))
        })
        {
            var location = new WorldSourceLocation(new(Guid.NewGuid(), file, ArtifactKind.Nbt), WorldSourceRole.SavedData, dimension);
            TextExtractionResult Extract(int version) => NbtTextExtractor.Extract(location,
                new("", new NbtCompound([new("DataVersion", new NbtInt(version)), new("data", new NbtCompound([field]))])));
            Require(Extract(4671) is { Complete: true, Occurrences.IsEmpty: true } && !Extract(4325).Complete,
                "known vanilla settings are recognized only in the layout version that stores them at this path");
        }
    }

    public static void ExactEvidenceAndSeparateFacets()
    {
        var java121 = MinecraftProfiles.ForDataVersion(3953)!;
        var modern = MinecraftProfiles.ForDataVersion(4774)!;
        Require(java121.Id == "java-1.21" && modern.Id == "java-26.1-snapshot-6", "known profiles identify exact verified releases, never directory labels");
        Require(java121.Features[ProfileFacet.Layout].Schema == "java-dimensions-directory"
            && modern.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced", "world layout is an independently declared facet");
        Require(java121.Features[ProfileFacet.TextSchema].Schema == "java-text-json"
            && modern.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt", "text representation is separate from layout and item schema");
        Require(java121.Lists.Encoding == LogicalListEncoding.Homogeneous && modern.Lists.Encoding == LogicalListEncoding.CompoundWrapper,
            "physical/list semantics use accepted profile-aware adapter");
        var intermediate = MinecraftProfiles.ForDataVersion(4325)!;
        Require(intermediate.Features[ProfileFacet.Layout].Schema == java121.Features[ProfileFacet.Layout].Schema
            && intermediate.Features[ProfileFacet.TextSchema].Schema == modern.Features[ProfileFacet.TextSchema].Schema,
            "1.21.5 retains java121 layout while independently using NBT text; no single modern switch");
        Require(java121.Supports(ProfileOperation.Read, ProfileFacet.Layout, ProfileFacet.TextSchema)
            && !java121.Supports(ProfileOperation.Write, ProfileFacet.Layout), "read/structural extraction does not imply accepted world write capability");
        foreach (var unknown in new[] { -1, 0, 3952, 3954, 4773, 4775, int.MaxValue })
            Require(MinecraftProfiles.ForDataVersion(unknown) is null, "unknown artifact version must never silently inherit a nearby profile");
        Require(MinecraftProfiles.ForDataVersion(4774)!.Features[ProfileFacet.CommandGrammar].Operations.Extract == SupportLevel.Unknown,
            "command grammar remains unaccepted until owning work package verifies it");
        Require(new[] { java121, modern, intermediate }.All(profile => profile.Features.Values.All(feature => feature.Operations.Extract == SupportLevel.Unknown)),
            "discovery-only profiles do not advertise text/item extraction before implementation");
    }
}
