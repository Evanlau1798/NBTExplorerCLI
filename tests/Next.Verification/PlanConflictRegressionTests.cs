using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class PlanConflictRegressionTests
{
    private static PatchPlan Plan(ArtifactAddress artifact, params SourceAddress[] targets)
        => new(World, PlanTests.Snapshot, "1", "1", targets.Select((address, index) => new TypedPatch(index.ToString(), address,
            TypedEdit.Replace(address.NodePath, new NbtInt(1), new NbtInt(index + 2)))),
            [new(artifact, ArtifactImage.Present(PlanTests.Preimage))], [new(artifact, OutputIntent.Write, ["scope"])], []);

    public static void DimensionCannotSplitPhysicalTargets()
    {
        var path = new TypedPath([PathSegment.ForKey("number")]);
        var file = PlanTests.Artifact;
        var normal = new SourceAddress(file, path, "number");
        var labeled = new SourceAddress(file, path, "other-role", "minecraft:the_nether");
        Reject(() => Plan(file, normal, normal));
        Reject(() => Plan(file, normal, labeled));
        var region = new ArtifactAddress(World, "region/r.0.0.mca", ArtifactKind.Region);
        var one = new SourceAddress(region, path, "number", "minecraft:overworld", new(0, 0));
        var alias = new SourceAddress(region, path, "number", "minecraft:the_nether", new(0, 0));
        Reject(() => Plan(region, one, alias));
        var separateChunk = new SourceAddress(region, path, "number", "minecraft:overworld", new(1, 0));
        Require(Plan(region, one, separateChunk).Operations.Length == 2, "同region不同chunk仍是不同物理目標");
    }

    public static void FinalReadbackMustBeConsistent()
    {
        var plan = PlanTests.Plan([PlanTests.Edit("a", "a"), PlanTests.Edit("b", "b")]);
        var prepared = new PreparedManifest(plan, [PlanTests.Candidate()]);
        var post = new ImageProof(ImageProofKind.Postimage, PlanTests.Artifact,
            ArtifactImage.Present(PlanTests.Postimage), ArtifactImage.Present(PlanTests.Postimage));
        var pre = new ImageProof(ImageProofKind.Preimage, PlanTests.Artifact,
            ArtifactImage.Present(PlanTests.Preimage), ArtifactImage.Present(PlanTests.Preimage));
        Require(prepared.Receipt(plan, 1, [new("a", OperationOutcome.Applied, proof: post),
            new("b", OperationOutcome.Unchanged, proof: post)], true).Success, "同檔一致postimage可以共用多筆結果");
        OperationReceipt[] contradictory = [new("a", OperationOutcome.Applied, proof: post), new("b", OperationOutcome.RolledBack, "rollback", pre)];
        Reject(() => prepared.Receipt(plan, 1, contradictory, true));
        Reject(() => new ApplyReceipt(plan.PlanHash, 1, ["a", "b"], contradictory, true));
        var alias = new ArtifactAddress(World, "LEVEL.DAT", ArtifactKind.Json);
        var aliasPre = new ImageProof(ImageProofKind.Preimage, alias, ArtifactImage.Present(PlanTests.Preimage), ArtifactImage.Present(PlanTests.Preimage));
        Reject(() => new ApplyReceipt(plan.PlanHash, 1, ["a", "b"],
            [contradictory[0], new("b", OperationOutcome.RolledBack, "aliased file", aliasPre)], true));
    }
}
