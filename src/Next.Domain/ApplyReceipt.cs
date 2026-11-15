using System.Collections.Immutable;

namespace Next.Domain;

public enum OperationOutcome { Applied, Unchanged, Skipped, Failed, Conflict, Unsupported, Cancelled, RecoveryRequired, RolledBack }
public enum ImageProofKind { Postimage, Preimage }
public sealed record ImageProof
{
    public ImageProofKind Kind { get; }
    public ArtifactAddress Artifact { get; }
    public ArtifactImage Expected { get; }
    public ArtifactImage Readback { get; }
    public ImageProof(ImageProofKind kind, ArtifactAddress artifact, ArtifactImage expected, ArtifactImage readback)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(readback);
        if (!Enum.IsDefined(kind) || expected != readback) throw new ArgumentException("Image readback must match its expected hash");
        if (artifact.ContainerEntry is not null) throw new ArgumentException("Readback proofs bind physical artifacts", nameof(artifact));
        (Kind, Artifact, Expected, Readback) = (kind, artifact, expected, readback);
    }
}
public sealed record OperationReceipt
{
    public string OperationId { get; }
    public OperationOutcome Outcome { get; }
    public string? Reason { get; }
    public ImmutableArray<ImageProof> Proofs { get; }
    public OperationReceipt(string operationId, OperationOutcome outcome, string? reason = null, ImageProof? proof = null)
        : this(operationId, outcome, reason, proof is null ? [] : new[] { proof }) { }
    public OperationReceipt(string operationId, OperationOutcome outcome, string? reason, IEnumerable<ImageProof> proofs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (outcome is not (OperationOutcome.Applied or OperationOutcome.Unchanged)) ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var required = outcome switch
        {
            OperationOutcome.Applied or OperationOutcome.Unchanged => ImageProofKind.Postimage,
            OperationOutcome.RolledBack => ImageProofKind.Preimage,
            _ => (ImageProofKind?)null
        };
        Proofs = PlanCollection.Copy(proofs).OrderBy(item => item.Artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray();
        if (required is null ? !Proofs.IsEmpty : Proofs.IsEmpty || Proofs.Any(proof => proof.Kind != required)
            || Proofs.Select(item => item.Artifact).Distinct().Count() != Proofs.Length)
            throw new ArgumentException("Terminal outcome requires matching physical image proofs, or no proofs for other outcomes", nameof(proofs));
        (OperationId, Outcome, Reason) = (operationId, outcome, reason);
    }
    public bool Equals(OperationReceipt? other) => other is not null && OperationId == other.OperationId && Outcome == other.Outcome
        && Reason == other.Reason && Proofs.SequenceEqual(other.Proofs);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OperationId); hash.Add(Outcome); hash.Add(Reason);
        foreach (var proof in Proofs) hash.Add(proof);
        return hash.ToHashCode();
    }
}

public sealed class ApplyReceipt
{
    public Hash256 PlanHash { get; }
    public long Revision { get; }
    public ImmutableArray<string> RequestedOperationIds { get; }
    public ImmutableArray<OperationReceipt> Results { get; }
    public bool DependenciesComplete { get; }
    public int Requested => RequestedOperationIds.Length;
    public ImmutableDictionary<OperationOutcome, int> Counts => Enum.GetValues<OperationOutcome>().ToImmutableDictionary(outcome => outcome,
        outcome => Results.Count(result => result.Outcome == outcome));
    public bool Success => DependenciesComplete && Results.All(result => result.Outcome is OperationOutcome.Applied or OperationOutcome.Unchanged);
    public ApplyReceipt(Hash256 planHash, long revision, IEnumerable<string> operationIds,
        IEnumerable<OperationReceipt> results, bool dependenciesComplete)
    {
        ArgumentNullException.ThrowIfNull(planHash);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentNullException.ThrowIfNull(operationIds);
        ArgumentNullException.ThrowIfNull(results);
        RequestedOperationIds = operationIds.Take(100_001).Order(StringComparer.Ordinal).ToImmutableArray();
        var frozenResults = results.Take(100_001).ToArray();
        if (RequestedOperationIds.Length > 100_000 || RequestedOperationIds.Any(string.IsNullOrWhiteSpace)
            || RequestedOperationIds.Distinct(StringComparer.Ordinal).Count() != RequestedOperationIds.Length
            || frozenResults.Length != RequestedOperationIds.Length || frozenResults.Any(result => result is null))
            throw new ArgumentException("Receipt must contain exactly one result for each unique requested operation");
        Results = frozenResults.OrderBy(result => result.OperationId, StringComparer.Ordinal).ToImmutableArray();
        if (!Results.Select(result => result.OperationId).SequenceEqual(RequestedOperationIds, StringComparer.Ordinal))
            throw new ArgumentException("Receipt operation set differs from the plan", nameof(results));
        var images = new Dictionary<Guid, Dictionary<string, ArtifactImage>>();
        foreach (var proof in Results.SelectMany(result => result.Proofs))
        {
            if (!images.TryGetValue(proof.Artifact.WorldId, out var world))
                images.Add(proof.Artifact.WorldId, world = new(StringComparer.OrdinalIgnoreCase));
            if (world.TryGetValue(proof.Artifact.RelativePath, out var image) && image != proof.Readback)
                throw new ArgumentException("One final receipt cannot claim different readbacks for the same physical artifact", nameof(results));
            world[proof.Artifact.RelativePath] = proof.Readback;
        }
        (PlanHash, Revision, DependenciesComplete) = (planHash, revision, dependenciesComplete);
    }
    public ApplyReceipt Revise(long revision, IEnumerable<OperationReceipt> results, bool dependenciesComplete)
    {
        if (revision <= Revision) throw new ArgumentOutOfRangeException(nameof(revision));
        var next = new ApplyReceipt(PlanHash, revision, RequestedOperationIds, results, dependenciesComplete);
        for (var index = 0; index < Results.Length; index++)
        {
            var previous = Results[index];
            var current = next.Results[index];
            var allowed = previous == current || previous.Outcome switch
            {
                OperationOutcome.RecoveryRequired => current.Outcome is OperationOutcome.Applied or OperationOutcome.Unchanged
                    or OperationOutcome.Failed or OperationOutcome.Conflict or OperationOutcome.Cancelled or OperationOutcome.RolledBack
                    or OperationOutcome.RecoveryRequired,
                OperationOutcome.Applied => current.Outcome is OperationOutcome.RolledBack or OperationOutcome.RecoveryRequired or OperationOutcome.Conflict,
                _ => false
            };
            if (!allowed) throw new ArgumentException("Terminal outcome cannot be reclassified without an unresolved recovery or rollback", nameof(results));
        }
        return next;
    }
}
