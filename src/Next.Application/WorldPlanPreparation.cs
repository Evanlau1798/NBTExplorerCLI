using Next.Domain;

namespace Next.Application;

public sealed class WorldPlanPreparation(IWorldInventory inventory, IArtifactStorage storage)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IArtifactStorage storage = storage ?? throw new ArgumentNullException(nameof(storage));
    public PreparedWorldPlan Prepare(VerifiedBackup backup, PatchPlan plan, bool worldOffline)
    {
        ArgumentNullException.ThrowIfNull(backup); CheckOffline(plan, worldOffline);
        if (plan.Operations.Any(operation => operation is not RestorePatch)) throw new ArgumentException("Raw restore requires explicit restore operations", nameof(plan));
        var bound = new WorldPlanBuilder(inventory).Build(backup, plan.Operations.Cast<RestorePatch>());
        if (bound.PlanHash != plan.PlanHash) throw new ArtifactConflictException("Restore plan differs from its exact verified backup binding");
        return PrepareOutputs(plan, (output, _) =>
        {
            var bytes = backup.ReadArtifact(output.Artifact);
            var expected = bytes is null ? ArtifactImage.Absent : ArtifactImage.Present(Hash256.Compute(bytes));
            return new(bytes ?? [], stream =>
            {
                var actual = output.Intent == OutputIntent.Delete ? ArtifactImage.Absent : ArtifactImage.Present(new Hash256(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))));
                if (actual != expected || output.Intent == OutputIntent.Delete && stream.Length != 0) throw new ArtifactConflictException("Staged raw restore differs from its verified target image");
            });
        });
    }
    public PreparedWorldPlan Prepare(WorldQuerySnapshot snapshot, PatchPlan plan, bool worldOffline)
    {
        ArgumentNullException.ThrowIfNull(snapshot); CheckOffline(plan, worldOffline);
        var bound = new WorldPlanBuilder(inventory).Bind(snapshot, plan.Operations);
        if (bound.Plan.PlanHash != plan.PlanHash) throw new ArtifactConflictException("Plan differs from its exact source/output policy");
        return PrepareOutputs(plan, (output, input) =>
        {
            var operations = plan.Operations.Where(operation => operation.RequiredOutputs.Contains(output.Artifact)).ToArray();
            if (input.Image.ExpectedAbsent) return WorldLanguageOutputs.Create(output.Artifact, operations, snapshot.Limits);
            var bytes = inventory.Read(output.Artifact);
            if (ArtifactImage.Present(Hash256.Compute(bytes)) != input.Image) throw new ArtifactConflictException("Source changed before preparation");
            return WorldArtifactEdits.Create(output.Artifact, bytes, operations, bound.Sources.Values.Where(source => source.Location.Artifact.RelativePath == output.Artifact.RelativePath).ToArray(), snapshot.Limits);
        });
    }
    private static void CheckOffline(PatchPlan plan, bool worldOffline)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!worldOffline && !plan.Outputs.IsEmpty) throw new ArgumentException("Candidate staging requires the caller's offline-world acknowledgement", nameof(worldOffline));
    }
    private PreparedWorldPlan PrepareOutputs(PatchPlan plan, Func<OutputDeclaration, ArtifactPrecondition, PreparedArtifactBytes> create)
    {
        var candidates = new List<IArtifactCandidate>(); var artifacts = new List<PreparedArtifact>();
        try
        {
            foreach (var output in plan.Outputs)
            {
                var input = plan.Preconditions.Single(item => item.Artifact == output.Artifact);
                var transformed = create(output, input);
                var candidate = output.Intent == OutputIntent.Delete ? storage.StageDelete(output.Artifact, input.Image) : storage.Stage(output.Artifact, input.Image, transformed.Bytes);
                candidates.Add(candidate); candidate.Flush(); candidate.Verify(transformed.Verify);
                artifacts.Add(new(output.Artifact, candidate.Postimage.ExpectedAbsent ? null : candidate.CandidatePath, candidate.Postimage, output.RequiredChecks.Select(name => new ValidationCheck(name, true, name == "preimage" ? input.Image.Hash ?? plan.PlanHash : candidate.Postimage.Hash ?? plan.PlanHash))));
            }
            // Parsed projections remain valid only while every staged postimage still matches.
            foreach (var candidate in candidates) candidate.Verify(_ => { });
            WorldPlanBuilder.CheckPreimages(inventory, plan);
            return new(plan, new PreparedManifest(plan, artifacts), candidates);
        }
        catch (Exception error)
        {
            try { PreparedWorldPlan.Cleanup(candidates); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); } throw;
        }
    }
}

public sealed class PreparedWorldPlan : IDisposable
{
    public PatchPlan Plan { get; }
    public PreparedManifest Manifest { get; }
    internal IReadOnlyList<IArtifactCandidate> Candidates { get; }
    internal PreparedWorldPlan(PatchPlan plan, PreparedManifest manifest, IReadOnlyList<IArtifactCandidate> candidates) => (Plan, Manifest, Candidates) = (plan, manifest, candidates);
    public void Dispose() => Cleanup(Candidates);
    internal static void Cleanup(IEnumerable<IArtifactCandidate> candidates)
    {
        List<Exception> failures = [];
        foreach (var candidate in candidates) try { candidate.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("Candidate cleanup failed", failures);
    }
}
