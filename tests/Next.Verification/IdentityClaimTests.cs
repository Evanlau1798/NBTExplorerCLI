using Next.Domain;

namespace Next.Verification;

internal static class IdentityClaimTests
{
    public static void RejectsMalformedCanonical()
    {
        var world = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var address = new SourceAddress(new(world, "level.dat", ArtifactKind.Nbt), TypedPath.Root, "literal");
        var occurrence = SourceIdentity.Occurrence(address);
        var group = SourceIdentity.Group("structural.book", address, 1);
        var valid = new[] { SourceIdentity.Artifact(address.Artifact), occurrence,
            SourceIdentity.Slot(occurrence, TypedPath.Root, "fragment"), group, SourceIdentity.Members(group, [occurrence], 1),
            SourceIdentity.Memory("same", "en_us", "zh_tw", "name", "", "1", "1"),
            SourceIdentity.Snapshot(world, [], SnapshotScope.All, new("1", "1", "1", "1")) };
        var rejected = new List<string>();
        foreach (var identity in valid)
        {
            var bytes = identity.Canonical.ToArray();
            var restored = new StableIdentity(identity.Kind, identity.Digest, bytes);
            if (!restored.IsValid || restored != identity) throw new InvalidOperationException("完整合法 claim 必須可還原");
            foreach (var malformed in new[] { bytes[..22], bytes[..^1], bytes.Concat(new byte[] { 0 }).ToArray() })
            {
                var claim = new StableIdentity(identity.Kind, Hash256.Compute(malformed), malformed);
                if (claim.IsValid || new IdentityRegistry().Register(claim) != IdentityRegistration.Invalid)
                    rejected.Add(identity.Kind + "/" + malformed.Length);
            }
        }
        byte[] header = [.. "nbtcli.next.identity"u8, 1, (byte)IdentityKind.Occurrence];
        var forged = new StableIdentity(IdentityKind.Occurrence, Hash256.Compute(header), header);
        try { SourceIdentity.Slot(forged, TypedPath.Root, "fragment"); rejected.Add("forged parent"); }
        catch (ArgumentException) { }
        if (rejected.Count != 0) throw new InvalidOperationException("Accepted malformed canonical: " + string.Join(", ", rejected));
    }

    public static void RejectsOversizedCanonical()
    {
        try { _ = new StableIdentity(IdentityKind.Artifact, new(new string('0', 64)), Enumerable.Repeat((byte)0, 67_108_865)); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("超出64 MiB的canonical必須拒絕");
    }
}
