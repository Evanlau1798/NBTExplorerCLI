using Next.Domain;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class MinecraftProfileTests
{
    public static void ExactEvidenceAndSeparateFacets()
    {
        var legacy = MinecraftProfiles.ForDataVersion(3953)!;
        var modern = MinecraftProfiles.ForDataVersion(4774)!;
        Require(legacy.Id == "java-1.21" && modern.Id == "java-26.1-snapshot-6", "known profiles identify exact verified releases, never directory labels");
        Require(legacy.Features[ProfileFacet.Layout].Schema == "java-dimensions-legacy"
            && modern.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced", "world layout is an independently declared facet");
        Require(legacy.Features[ProfileFacet.TextSchema].Schema == "java-text-json"
            && modern.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt", "text representation is separate from layout and item schema");
        Require(legacy.Lists.Encoding == LogicalListEncoding.Homogeneous && modern.Lists.Encoding == LogicalListEncoding.CompoundWrapper,
            "physical/list semantics use accepted profile-aware adapter");
        var intermediate = MinecraftProfiles.ForDataVersion(4325)!;
        Require(intermediate.Features[ProfileFacet.Layout].Schema == legacy.Features[ProfileFacet.Layout].Schema
            && intermediate.Features[ProfileFacet.TextSchema].Schema == modern.Features[ProfileFacet.TextSchema].Schema,
            "1.21.5 retains legacy layout while independently using NBT text; no single modern switch");
        Require(legacy.Supports(ProfileOperation.Read, ProfileFacet.Layout, ProfileFacet.TextSchema)
            && !legacy.Supports(ProfileOperation.Write, ProfileFacet.Layout), "read/structural extraction does not imply accepted world write capability");
        foreach (var unknown in new[] { -1, 0, 3952, 3954, 4773, 4775, int.MaxValue })
            Require(MinecraftProfiles.ForDataVersion(unknown) is null, "unknown artifact version must never silently inherit a nearby profile");
        Require(MinecraftProfiles.ForDataVersion(4774)!.Features[ProfileFacet.CommandGrammar].Operations.Extract == SupportLevel.Unknown,
            "command grammar remains unaccepted until owning work package verifies it");
        Require(new[] { legacy, modern, intermediate }.All(profile => profile.Features.Values.All(feature => feature.Operations.Extract == SupportLevel.Unknown)),
            "discovery-only profiles do not advertise text/item extraction before implementation");
    }
}
