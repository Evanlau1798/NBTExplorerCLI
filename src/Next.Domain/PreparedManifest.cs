using System.Collections.Immutable;

namespace Next.Domain;

public sealed record ValidationCheck(string Name, bool Passed, Hash256 EvidenceHash);
public sealed class PreparedArtifact
{
    public ArtifactAddress Artifact { get; }
    public string? CandidatePath { get; }
    public ArtifactImage Postimage { get; }
    public ImmutableArray<ValidationCheck> Checks { get; }
    public PreparedArtifact(ArtifactAddress artifact, string? candidatePath, ArtifactImage postimage, IEnumerable<ValidationCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(postimage);
        if (candidatePath is not null && (string.IsNullOrWhiteSpace(candidatePath) || candidatePath.Contains('\0')))
            throw new ArgumentException("Invalid candidate path", nameof(candidatePath));
        Checks = PlanCollection.Copy(checks, 128).OrderBy(check => check.Name, StringComparer.Ordinal).ToImmutableArray();
        if (Checks.Any(check => string.IsNullOrWhiteSpace(check.Name) || check.EvidenceHash is null)
            || Checks.Select(check => check.Name).Distinct(StringComparer.Ordinal).Count() != Checks.Length)
            throw new ArgumentException("Invalid validation evidence", nameof(checks));
        (Artifact, CandidatePath, Postimage) = (artifact, candidatePath, postimage);
    }
}
public sealed class PreparedManifest
{
    public const string SchemaFamily = "nbtcli.next.prepared-manifest";
    public const int SchemaVersion = 1;
    public Hash256 PlanHash { get; }
    public ImmutableArray<PreparedArtifact> Artifacts { get; }
    public Hash256 ManifestHash { get; }
    public PreparedManifest(PatchPlan plan, IEnumerable<PreparedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Artifacts = PlanCollection.Copy(artifacts).OrderBy(artifact => artifact.Artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray();
        if (Artifacts.Length != plan.Outputs.Length || Artifacts.Select(item => item.Artifact).Distinct().Count() != Artifacts.Length
            || Artifacts.Where(item => item.CandidatePath is not null).Select(item => item.CandidatePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != Artifacts.Count(item => item.CandidatePath is not null))
            throw new ArgumentException("Prepared outputs must uniquely match the plan", nameof(artifacts));
        for (var index = 0; index < Artifacts.Length; index++)
        {
            var artifact = Artifacts[index];
            var output = plan.Outputs[index];
            if (artifact.Artifact != output.Artifact || artifact.Checks.Any(check => !check.Passed)
                || !artifact.Checks.Select(check => check.Name).SequenceEqual(output.RequiredChecks, StringComparer.Ordinal)
                || (output.Intent == OutputIntent.Delete) != artifact.Postimage.ExpectedAbsent
                || artifact.Postimage.ExpectedAbsent != (artifact.CandidatePath is null))
                throw new ArgumentException("Prepared output lacks matching image, candidate or required validation", nameof(artifacts));
        }
        var preparedImages = Artifacts.ToDictionary(artifact => artifact.Artifact, artifact => artifact.Postimage);
        foreach (var restore in plan.Operations.OfType<RestorePatch>())
            foreach (var target in restore.Restore.AuthorizedArtifacts)
                if (preparedImages[target.Artifact] != target.Image)
                    throw new ArgumentException("Prepared restore postimage differs from the verified backup authorization", nameof(artifacts));
        PlanHash = plan.PlanHash;
        ManifestHash = PlanEncoding.Hash(this);
    }
    public ApplyReceipt Receipt(PatchPlan plan, long revision, IEnumerable<OperationReceipt> results, bool dependenciesComplete)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.PlanHash != PlanHash) throw new ArgumentException("Prepared manifest belongs to a different plan", nameof(plan));
        var receipt = new ApplyReceipt(PlanHash, revision, plan.Operations.Select(operation => operation.Id), results, dependenciesComplete);
        var postimages = Artifacts.ToDictionary(artifact => artifact.Artifact, artifact => artifact.Postimage);
        var preimages = plan.Preconditions.ToDictionary(artifact => artifact.Artifact, artifact => artifact.Image);
        for (var index = 0; index < receipt.Results.Length; index++)
        {
            var result = receipt.Results[index];
            if (result.Proofs.IsEmpty) continue;
            var required = plan.Operations[index].RequiredOutputs.Distinct().ToHashSet();
            if (!required.SetEquals(result.Proofs.Select(proof => proof.Artifact)))
                throw new ArgumentException("Receipt proofs must cover exactly the operation's physical outputs", nameof(results));
            foreach (var proof in result.Proofs)
            {
                var expected = proof.Kind == ImageProofKind.Postimage ? postimages[proof.Artifact] : preimages[proof.Artifact];
                if (proof.Expected != expected) throw new ArgumentException("Receipt proof differs from its bound pre/postimage", nameof(results));
            }
        }
        return receipt;
    }
}
public sealed record JournalBinding(Hash256 PlanHash, Hash256 ManifestHash)
{
    public static JournalBinding From(PreparedManifest prepared) => new(prepared.PlanHash, prepared.ManifestHash);
}
