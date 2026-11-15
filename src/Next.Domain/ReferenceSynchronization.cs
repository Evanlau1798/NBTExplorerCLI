using System.Collections.Immutable;

namespace Next.Domain;

public enum ReferenceCompleteness { Complete, Incomplete, Unresolved }

public sealed record ReferenceMember
{
    public SourceAddress Address { get; }
    public string ExpectedText { get; }
    public string ReplacementText { get; }
    public string GrammarProfile { get; }
    public int GrammarVersion { get; }
    public StableIdentity Occurrence => SourceIdentity.Occurrence(Address);
    public ReferenceMember(SourceAddress address, string expectedText, string replacementText, string grammarProfile, int grammarVersion)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(expectedText);
        ArgumentNullException.ThrowIfNull(replacementText);
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarProfile);
        ArgumentOutOfRangeException.ThrowIfLessThan(grammarVersion, 1);
        if (address.Syntax is not null && (address.Syntax.GrammarProfile != grammarProfile || address.Syntax.GrammarVersion != grammarVersion))
            throw new ArgumentException("Reference grammar must match its syntax locator");
        (Address, ExpectedText, ReplacementText, GrammarProfile, GrammarVersion)
            = (address, expectedText, replacementText, grammarProfile, grammarVersion);
    }
}

public static class ReferenceSynchronizations
{
    public static NbtDocument Apply(SourceAddress document, NbtDocument source, ReferenceSynchronization synchronization,
        IEnumerable<ReferenceMember> proposed)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        NbtDocumentScope.RequireDocument(document);
        if (!synchronization.Authorizes(proposed))
            throw new EditRejectedException(EditFailure.Precondition, "Reference synchronization requires its complete exact member declaration");
        var selected = synchronization.Members.Where(member => member.Address.Artifact == document.Artifact
            && member.Address.Dimension == document.Dimension && member.Address.Chunk == document.Chunk).ToArray();
        if (selected.Length == 0) throw new EditRejectedException(EditFailure.InvalidPath, "Document has no declared reference members");
        return TypedTreeEdits.Apply(source, selected.Select(member =>
        {
            NbtDocumentScope.RequireTarget(document, member.Address);
            return TypedEdit.Replace(member.Address.NodePath, new NbtString(member.ExpectedText), new NbtString(member.ReplacementText));
        }));
    }
    public static bool Verify(SourceAddress document, NbtDocument source, NbtDocument candidate, ReferenceSynchronization synchronization,
        IEnumerable<ReferenceMember> proposed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Apply(document, source, synchronization, proposed) == candidate;
    }
}

public sealed class ReferenceSynchronization
{
    public StableIdentity GroupId { get; }
    public long GroupRevision { get; }
    public Hash256 MemberDigest { get; }
    public ReferenceCompleteness Completeness { get; }
    public ImmutableArray<ReferenceMember> Members { get; }
    public ReferenceSynchronization(StableIdentity groupId, long groupRevision, Hash256 memberDigest,
        ReferenceCompleteness completeness, IEnumerable<ReferenceMember> members)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(memberDigest);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentOutOfRangeException.ThrowIfNegative(groupRevision);
        if (groupId.Kind != IdentityKind.Group || !groupId.IsValid || !Enum.IsDefined(completeness))
            throw new ArgumentException("Expected a valid group identity and coverage state");
        Members = members.Take(100_001).ToImmutableArray();
        if (Members.Length is 0 or > 100_000 || Members.Any(member => member is null))
            throw new ArgumentException("Expected a nonempty bounded reference group", nameof(members));
        var world = IdentityCanonical.ReadWorld(groupId);
        if (Members.Any(member => member.Address.Artifact.WorldId != world))
            throw new ArgumentException("Reference members must belong to the group world", nameof(members));
        var occurrences = Members.Select(member => member.Occurrence).ToArray();
        if (occurrences.Select(occurrence => occurrence.Digest).Distinct().Count() != occurrences.Length
            || SourceIdentity.Members(groupId, occurrences, groupRevision).Digest != memberDigest)
            throw new ArgumentException("Reference membership differs from its declared digest", nameof(memberDigest));
        (GroupId, GroupRevision, MemberDigest, Completeness) = (groupId, groupRevision, memberDigest, completeness);
    }
    public bool Authorizes(IEnumerable<ReferenceMember> proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        return Completeness == ReferenceCompleteness.Complete && Members.SequenceEqual(proposed.Take(Members.Length + 1));
    }
}
