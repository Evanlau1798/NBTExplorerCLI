using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class PreparedReceiptTests
{
    private static ImageProof Proof(ArtifactAddress artifact, ArtifactImage image, ImageProofKind kind = ImageProofKind.Postimage)
        => new(kind, artifact, image, image);
    public static void ExactPreparedImageBinding()
    {
        var plan = PlanTests.Plan();
        var prepared = new PreparedManifest(plan, [PlanTests.Candidate()]);
        var result = new OperationReceipt("edit", OperationOutcome.Applied, proof: Proof(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Postimage)));
        Require(prepared.Receipt(plan, 1, [result], true).Success, "applied需綁實際prepared postimage");
        Reject(() => prepared.Receipt(plan, 1, [new("edit", OperationOutcome.Applied,
            proof: Proof(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage)))], true));
        Reject(() => prepared.Receipt(plan, 1, [new("edit", OperationOutcome.Applied,
            proof: Proof(new(World, "other.dat", ArtifactKind.Nbt), ArtifactImage.Present(PlanTests.Postimage)))], true));
        Reject(() => prepared.Receipt(PlanTests.Plan(policy: "2"), 1, [result], true));
        Reject(() => plan.Receipt(1, [result], true));
    }

    public static void AbsenceAndMultipleArtifacts()
    {
        var restore = new ArtifactRestore(PlanTests.Preimage,
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Postimage))],
            new Dictionary<ArtifactAddress, ArtifactImage> { [PlanTests.Artifact] = ArtifactImage.Absent });
        var plan = new PatchPlan(World, PlanTests.Snapshot, "1", "1", [new RestorePatch("restore", restore)],
            [new(PlanTests.Artifact, ArtifactImage.Absent)], [new(PlanTests.Artifact, OutputIntent.Write, ["scope"])], []);
        var prepared = new PreparedManifest(plan, [new(PlanTests.Artifact, "stage/new.dat", ArtifactImage.Present(PlanTests.Postimage),
            [new("scope", true, PlanTests.Preimage)])]);
        var rolledBack = new OperationReceipt("restore", OperationOutcome.RolledBack, "verified absence",
            Proof(PlanTests.Artifact, ArtifactImage.Absent, ImageProofKind.Preimage));
        Require(!prepared.Receipt(plan, 2, [rolledBack], true).Success, "還原新建檔為不存在也能明示證明，不是原apply成功");
        Reject(() => prepared.Receipt(plan, 2, [new("restore", OperationOutcome.RolledBack, "wrong preimage",
            Proof(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage), ImageProofKind.Preimage))], true));
        var second = new ArtifactAddress(World, "data/second.dat", ArtifactKind.Nbt);
        var memberA = new ReferenceMember(Address(new([PathSegment.ForKey("name")])), "Bob", "Alice", "synthetic", 1);
        var memberB = new ReferenceMember(new(second, new([PathSegment.ForKey("name")]), "reference"), "Bob", "Alice", "synthetic", 1);
        var group = SourceIdentity.Group("inferred.name", memberA.Address, 1);
        var members = new[] { memberA, memberB };
        var patch = new ReferencePatch("references", new(group, 1, SourceIdentity.Members(group, members.Select(member => member.Occurrence), 1).Digest,
            ReferenceCompleteness.Complete, members));
        var multi = new PatchPlan(World, PlanTests.Snapshot, "1", "1", [patch],
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage)), new(second, ArtifactImage.Present(PlanTests.Preimage))],
            [new(PlanTests.Artifact, OutputIntent.Write, ["scope"]), new(second, OutputIntent.Write, ["scope"])], []);
        var staged = new PreparedManifest(multi, multi.Outputs.Select(output => new PreparedArtifact(output.Artifact,
            "stage/" + output.Artifact.RelativePath, ArtifactImage.Present(PlanTests.Postimage), [new("scope", true, PlanTests.Preimage)])));
        var proofs = staged.Artifacts.Select(artifact => Proof(artifact.Artifact, artifact.Postimage)).ToArray();
        Require(staged.Receipt(multi, 1, [new("references", OperationOutcome.Applied, null, proofs.Reverse())], true).Success,
            "跨檔operation需逐artifact proof，次序不影響語義");
        Reject(() => staged.Receipt(multi, 1, [new("references", OperationOutcome.Applied, null, proofs.Take(1))], true));
    }
}
