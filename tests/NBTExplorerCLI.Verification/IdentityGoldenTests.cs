using NBTExplorerCLI.Domain;
using System.Globalization;

namespace NBTExplorerCLI.Verification;

internal static class IdentityGoldenTests
{
    public static void Vectors(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var world = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var address = new SourceAddress(new(world, "level.dat", ArtifactKind.Nbt),
                new([PathSegment.ForKey("text")]), "literal");
            var artifact = SourceIdentity.Artifact(address.Artifact);
            var occurrence = SourceIdentity.Occurrence(address);
            var slot = SourceIdentity.Slot(occurrence,
                new([PathSegment.ForKey(""), PathSegment.ForIndex(0), PathSegment.ForKey("\ud800")]), "text.fragment");
            var group = SourceIdentity.Group("structural.book", address, 1);
            var members = SourceIdentity.Members(group, [occurrence, slot], 2);
            var memory = SourceIdentity.Memory("same", "en_us", "zh_tw", "name", "", "1", "1");
            var snapshot = SourceIdentity.Snapshot(world,
                [new(address.Artifact, Hash256.Compute([1, 2, 3]), SourceOutcome.Success)],
                SnapshotScope.All, new("1", "1", "1", "1"));
            // Independent Python struct/hashlib vectors for the frozen domain encoding.
            (StableIdentity Identity, int Length, string Digest)[] vectors =
            [
                (artifact, 117, "16fec223e566ce244acec5ed1db74c6730a0a8dac40742a5b8bb5d2a92836271"),
                (occurrence, 179, "555d34097076fb19f3da8ff91d03d3f5274f39cfba30527ce5dec101b1c22fa5"),
                (slot, 252, "ff8b6d9605aa40592d45949426e70074814036980e4a061d7d3a5cc48c4c4989"),
                (group, 199, "4afcec8b0bfbac212c1a7eb3c4e86af7ada669e86f39249c6a3c300dbaafb27b"),
                (members, 671, "e93feea4333c9340fc1045447ab1eb4bcd183a42403586a6fe5de9a0cfba455b"),
                (memory, 85, "9b8ee893fa58679c91ba8cbcfbed7a325eaa02e72b25ee00673da2ac459d2a3a"),
                (snapshot, 393, "8ccd5182147af29ff6f798189c94d5d93182e1f552bb855159aa2aa596cb8bb0")
            ];
            foreach (var (identity, length, digest) in vectors)
                if (!identity.IsValid || identity.Canonical.Length != length || identity.Digest.Hex != digest)
                    throw new InvalidOperationException($"{culture}/{identity.Kind} canonical vector mismatch");
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
