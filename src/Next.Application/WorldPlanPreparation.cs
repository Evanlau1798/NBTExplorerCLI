using Next.Domain;

namespace Next.Application;

public sealed class WorldPlanPreparation(IWorldInventory inventory, IArtifactStorage storage)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IArtifactStorage storage = storage ?? throw new ArgumentNullException(nameof(storage));
    public PreparedWorldPlan Prepare(WorldQuerySnapshot snapshot, PatchPlan plan, bool worldOffline)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(plan);
        if (!worldOffline && !plan.Outputs.IsEmpty) throw new ArgumentException("Candidate staging requires the caller's offline-world acknowledgement", nameof(worldOffline));
        var bound = new WorldPlanBuilder(inventory).Bind(snapshot, plan.Operations);
        if (bound.Plan.PlanHash != plan.PlanHash) throw new ArtifactConflictException("Plan differs from its exact source/output policy");
        var candidates = new List<IArtifactCandidate>(); var artifacts = new List<PreparedArtifact>();
        try
        {
            foreach (var output in plan.Outputs)
            {
                var operations = plan.Operations.Where(operation => operation.RequiredOutputs.Contains(output.Artifact)).ToArray();
                var input = plan.Preconditions.Single(item => item.Artifact == output.Artifact);
                var bytes = inventory.Read(output.Artifact);
                if (ArtifactImage.Present(Hash256.Compute(bytes)) != input.Image) throw new ArtifactConflictException("Source changed before preparation");
                var transformed = WorldArtifactEdits.Create(output.Artifact, bytes, operations, bound.Sources[output.Artifact], snapshot.Limits);
                var candidate = storage.Stage(output.Artifact, input.Image, transformed.Bytes); candidates.Add(candidate); candidate.Flush(); candidate.Verify(transformed.Verify);
                artifacts.Add(new(output.Artifact, candidate.CandidatePath, candidate.Postimage, output.RequiredChecks.Select(name => new ValidationCheck(name, true, name == "preimage" ? input.Image.Hash! : candidate.Postimage.Hash!))));
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
