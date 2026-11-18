using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public enum OutputIntent { Write, Delete }
public enum RecoveryStrategy { RollForwardPreferred, RollbackPreferred }
public sealed record ArtifactPrecondition(ArtifactAddress Artifact, ArtifactImage Image);
public sealed record ArtifactDependency(ArtifactAddress Consumer, ArtifactAddress Required);
public sealed class OutputDeclaration
{
    public ArtifactAddress Artifact { get; }
    public OutputIntent Intent { get; }
    public ImmutableArray<string> RequiredChecks { get; }
    public OutputDeclaration(ArtifactAddress artifact, OutputIntent intent, IEnumerable<string> requiredChecks)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!Enum.IsDefined(intent)) throw new ArgumentOutOfRangeException(nameof(intent));
        RequiredChecks = PlanCollection.Copy(requiredChecks, 128).Order(StringComparer.Ordinal).ToImmutableArray();
        if (RequiredChecks.IsEmpty || RequiredChecks.Any(string.IsNullOrWhiteSpace)
            || RequiredChecks.Distinct(StringComparer.Ordinal).Count() != RequiredChecks.Length)
            throw new ArgumentException("Output requires distinct named validation checks", nameof(requiredChecks));
        (Artifact, Intent) = (artifact, intent);
    }
}
public sealed class PatchPlan
{
    public const string SchemaFamily = "nbtcli.patch-plan";
    public const int SchemaVersion = 1;
    public Guid WorldId { get; }
    public StableIdentity SnapshotId { get; }
    public string ProfileVersion { get; }
    public string PolicyVersion { get; }
    public RecoveryStrategy Recovery { get; }
    public ImmutableArray<PatchOperation> Operations { get; }
    public ImmutableArray<ArtifactPrecondition> Preconditions { get; }
    public ImmutableArray<OutputDeclaration> Outputs { get; }
    public ImmutableArray<ArtifactDependency> Dependencies { get; }
    public Hash256 PlanHash { get; }
    public PatchPlan(Guid worldId, StableIdentity snapshotId, string profileVersion, string policyVersion,
        IEnumerable<PatchOperation> operations, IEnumerable<ArtifactPrecondition> preconditions,
        IEnumerable<OutputDeclaration> outputs, IEnumerable<ArtifactDependency> dependencies,
        RecoveryStrategy recovery = RecoveryStrategy.RollForwardPreferred)
    {
        ArgumentNullException.ThrowIfNull(snapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        if (!Enum.IsDefined(recovery)) throw new ArgumentOutOfRangeException(nameof(recovery));
        Recovery = recovery;
        (WorldId, SnapshotId, ProfileVersion, PolicyVersion, Operations, Preconditions, Outputs, Dependencies)
            = (worldId, snapshotId, profileVersion, policyVersion,
                PlanCollection.Copy(operations).OrderBy(operation => operation.Id, StringComparer.Ordinal).ToImmutableArray(),
                PlanCollection.Copy(preconditions).OrderBy(item => item.Artifact?.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
                PlanCollection.Copy(outputs).OrderBy(item => item.Artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
                PlanCollection.Copy(dependencies));
        var inferred = Operations.OfType<KeyConversionPatch>().Select(operation => new ArtifactDependency(
            PlanArtifacts.Outer(operation.Conversion.Address.Artifact), PlanArtifacts.Outer(operation.Conversion.Dependency.LanguageArtifact)))
            .Where(dependency => dependency.Consumer != dependency.Required);
        Dependencies = Dependencies.Concat(inferred).Distinct().OrderBy(item => item.Consumer?.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.Required?.RelativePath, StringComparer.Ordinal).ToImmutableArray();
        PlanValidation.Validate(this);
        PlanHash = PlanEncoding.Hash(this);
    }
    public ApplyReceipt Receipt(long revision, IEnumerable<OperationReceipt> results, bool dependenciesComplete)
    {
        var receipt = new ApplyReceipt(PlanHash, revision, Operations.Select(operation => operation.Id), results, dependenciesComplete);
        if (receipt.Results.Any(result => !result.Proofs.IsEmpty)) throw new ArgumentException("Image outcomes require a bound PreparedManifest", nameof(results));
        return receipt;
    }
}

internal static class PlanCollection
{
    public static ImmutableArray<T> Copy<T>(IEnumerable<T> source, int maximum = 100_000) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = source.Take(maximum + 1).ToImmutableArray();
        if (values.Length > maximum || values.Any(value => value is null)) throw new ArgumentException("Invalid bounded contract collection", nameof(source));
        return values;
    }
}
