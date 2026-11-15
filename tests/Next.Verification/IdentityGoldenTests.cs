using Next.Domain;
using System.Globalization;

namespace Next.Verification;

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
            // Independent Python struct/hashlib vectors; inputs and encoding are frozen in docs/domain-contracts.md.
            (StableIdentity Identity, int Length, string Digest)[] vectors =
            [
                (artifact, 122, "3bc0b827727a2420fdfb8382dcbe487d01bbbffda09f6f7d285feec3a7dff00f"),
                (occurrence, 189, "64caca3ac0c3c2f794582fe95a024a987bed3628d600c70776c1151db1ecc06f"),
                (slot, 267, "8ff3bf40f09fbf73bd13e80aa486e8a0177bb6da872206dc2e6f64aafae70c2e"),
                (group, 209, "c0eb641692a07b51e0527e23de3a20504ee36e3a48ffd3819315dcabdfb668f1"),
                (members, 711, "e75af3620dfdc21c0e6fde810b4129837a0a1c506bdb64fe9cdb5bd2b369d1da"),
                (memory, 90, "d754a175424deba34579097801b1c65872df01a1c782a57c23bd808dfc4a1942"),
                (snapshot, 403, "3bf774fdbae8b3e0e228edf137ea072dd18e2b3ac748b90bf279c3e24b6a01f8")
            ];
            foreach (var (identity, length, digest) in vectors)
                if (!identity.IsValid || identity.Canonical.Length != length || identity.Digest.Hex != digest)
                    throw new InvalidOperationException($"{culture}/{identity.Kind} canonical vector mismatch");
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
