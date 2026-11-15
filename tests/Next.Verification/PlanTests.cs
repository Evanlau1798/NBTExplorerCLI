using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class PlanTests
{
    internal static readonly ArtifactAddress Artifact = new(World, "level.dat", ArtifactKind.Nbt);
    internal static readonly Hash256 Preimage = Hash256.Compute([1]);
    internal static readonly Hash256 Postimage = Hash256.Compute([2]);
    internal static StableIdentity Snapshot => SourceIdentity.Snapshot(World,
        [new(Artifact, Preimage, SourceOutcome.Success)], SnapshotScope.All, new("1", "1", "1", "1"));
    internal static TypedPatch Edit(string id = "edit", string key = "number", NbtValue? replacement = null)
    {
        var path = new TypedPath([PathSegment.ForKey(key)]);
        return new(id, Address(path), TypedEdit.Replace(path, new NbtInt(1), replacement ?? new NbtInt(2)));
    }
    internal static PatchPlan Plan(IEnumerable<PatchOperation>? operations = null, string policy = "1")
        => new(World, Snapshot, "1", policy, operations ?? [Edit()],
            [new(Artifact, ArtifactImage.Present(Preimage))], [new(Artifact, OutputIntent.Write, ["parse", "scope"])], []);
    internal static PreparedArtifact Candidate(string path = "stage/a.dat", Hash256? postimage = null)
        => new(Artifact, path, ArtifactImage.Present(postimage ?? Postimage),
            [new("scope", true, Hash256.Compute([4])), new("parse", true, Hash256.Compute([3]))]);

    public static void SemanticHashAndImmutableOrder()
    {
        var operations = new List<PatchOperation> { Edit("b", "b"), Edit("a", "a") };
        var one = Plan(operations);
        var two = Plan(operations.AsEnumerable().Reverse());
        operations.Clear();
        Require(one.PlanHash == two.PlanHash && one.Operations.Select(operation => operation.Id).SequenceEqual(["a", "b"]),
            "同義plan必須不依列舉順序且凍結operation集合");
        Require(one.PlanHash != Plan([Edit("a", "a"), Edit("b", "b", new NbtLong(2))]).PlanHash, "型別改變必須改planHash");
        Require(one.PlanHash != Plan(one.Operations, "2").PlanHash, "policy版本是plan語義");
        var otherRecovery = new PatchPlan(one.WorldId, one.SnapshotId, one.ProfileVersion, one.PolicyVersion,
            one.Operations, one.Preconditions, one.Outputs, one.Dependencies, RecoveryStrategy.RollbackPreferred);
        Require(one.PlanHash != otherRecovery.PlanHash, "明示recovery strategy是不可變plan語義");
        Require(Plan([Edit(replacement: new NbtFloat(0))]).PlanHash != Plan([Edit(replacement: new NbtFloat(int.MinValue))]).PlanHash,
            "浮點raw bits必須參與planHash");
    }

    public static void RejectsConflictsAndUnboundOutputs()
    {
        Reject(() => Plan([Edit(), Edit()]));
        Reject(() => Plan([Edit("a"), Edit("b")]));
        Reject(() => new PatchPlan(World, Snapshot, "1", "1", [Edit()], [], [new(Artifact, OutputIntent.Write, ["scope"])], []));
        Reject(() => new PatchPlan(World, Snapshot, "1", "1", [Edit()], [new(Artifact, ArtifactImage.Present(Preimage))], [], []));
        Reject(() => new PatchPlan(Guid.NewGuid(), Snapshot, "1", "1", [Edit()],
            [new(Artifact, ArtifactImage.Present(Preimage))], [new(Artifact, OutputIntent.Write, ["scope"])], []));
        Reject(() => new PatchPlan(World, SourceIdentity.Artifact(Artifact), "1", "1", [Edit()],
            [new(Artifact, ArtifactImage.Present(Preimage))], [new(Artifact, OutputIntent.Write, ["scope"])], []));
        Reject(() => new PatchPlan(World, Snapshot, "1", "1", [Edit()], [new(Artifact, ArtifactImage.Absent)],
            [new(Artifact, OutputIntent.Write, ["scope"])], []));
    }

    public static void PreparedBindsPathsAndChecks()
    {
        var plan = Plan();
        var a = new PreparedManifest(plan, [Candidate()]);
        var b = new PreparedManifest(plan, [Candidate("stage/b.dat")]);
        Require(a.PlanHash == b.PlanHash && a.PlanHash == plan.PlanHash && a.ManifestHash != b.ManifestHash,
            "候選路徑不能影響planHash，但prepared必須綁定各自路徑");
        Require(a.ManifestHash != new PreparedManifest(plan, [Candidate(postimage: Hash256.Compute([9]))]).ManifestHash,
            "候選內容變更必須重新綁定manifest");
        Require(JournalBinding.From(a) == new JournalBinding(plan.PlanHash, a.ManifestHash), "journal須綁兩種hash");
        Reject(() => new PreparedManifest(plan, []));
        Reject(() => new PreparedManifest(plan, [Candidate(), Candidate()]));
        Reject(() => new PreparedManifest(plan, [new(Artifact, "stage/a.dat", ArtifactImage.Present(Postimage),
            [new("parse", true, Preimage), new("scope", false, Preimage)])]));
        Reject(() => new PreparedManifest(plan, [new(Artifact, "stage/a.dat", ArtifactImage.Present(Postimage), [new("parse", true, Preimage)])]));
        Reject(() => new PreparedManifest(plan, [new(Artifact, null, ArtifactImage.Absent, [new("parse", true, Preimage), new("scope", true, Preimage)])]));
    }

    public static void ReceiptUsesPlanSet()
    {
        var plan = Plan([Edit("a", "a"), Edit("b", "b")]);
        var receipt = plan.Receipt(1, [new("a", OperationOutcome.Failed, "failed"), new("b", OperationOutcome.Cancelled, "cancelled")], false);
        Require(receipt.PlanHash == plan.PlanHash && receipt.Requested == 2, "requested只能取自實際plan operation集合");
        Reject(() => plan.Receipt(1, [new("a", OperationOutcome.Failed, "failed")], false));
        Reject(() => plan.Receipt(1, [new("a", OperationOutcome.Failed, "failed"), new("extra", OperationOutcome.Failed, "failed")], false));
    }
}
