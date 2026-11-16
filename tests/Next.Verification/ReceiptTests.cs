using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class ReceiptTests
{
    private static readonly Hash256 Hash = Hash256.Compute([1, 2, 3]);
    private static readonly ImageProof Post = new(ImageProofKind.Postimage, PlanTests.Artifact, ArtifactImage.Present(Hash), ArtifactImage.Present(Hash));
    private static readonly ImageProof Pre = new(ImageProofKind.Preimage, PlanTests.Artifact, ArtifactImage.Present(Hash), ArtifactImage.Present(Hash));
    private static OperationReceipt Result(string id, OperationOutcome outcome) => new(id, outcome, "evidence",
        outcome is OperationOutcome.Applied or OperationOutcome.Unchanged ? Post : outcome == OperationOutcome.RolledBack ? Pre : null);

    public static void NineStateConservation()
    {
        var results = Enum.GetValues<OperationOutcome>().Select(outcome => Result(outcome.ToString(), outcome)).ToArray();
        var receipt = new ApplyReceipt(Hash, 1, results.Select(result => result.OperationId), results.Reverse(), true);
        Require(receipt.Requested == 9 && receipt.Counts.Count == 9 && receipt.Counts.Values.Sum() == 9
            && receipt.Counts.Values.All(count => count == 1), "九種終態必須逐項互斥且requested守恆");
        Require(!receipt.Success, "混合失敗／復原狀態不可回apply成功");
        var success = new ApplyReceipt(Hash, 1, ["a", "b"], [Result("b", OperationOutcome.Unchanged), Result("a", OperationOutcome.Applied)], true);
        Require(success.Success && !new ApplyReceipt(Hash, 1, ["a"], [Result("a", OperationOutcome.Applied)], false).Success,
            "成功需全部applied/unchanged且依賴完成");
        Require(success.Results.Select(result => result.OperationId).SequenceEqual(["a", "b"]), "回執以operationId canonical排序");
    }

    public static void RejectsInvalidAccountingAndProofs()
    {
        Reject(() => new ApplyReceipt(Hash, 1, ["a", "b"], [Result("a", OperationOutcome.Failed)], true));
        Reject(() => new ApplyReceipt(Hash, 1, ["a"], [Result("a", OperationOutcome.Failed), Result("a", OperationOutcome.Failed)], true));
        Reject(() => new ApplyReceipt(Hash, 1, ["a"], [Result("extra", OperationOutcome.Failed)], true));
        Reject(() => new ApplyReceipt(Hash, 1, ["a", "a"], [Result("a", OperationOutcome.Failed)], true));
        Reject(() => new ApplyReceipt(Hash, -1, [], [], true));
        Reject(() => new OperationReceipt("a", (OperationOutcome)99, "unknown"));
        Reject(() => new OperationReceipt("a", OperationOutcome.Applied));
        Reject(() => new OperationReceipt("a", OperationOutcome.Unchanged, proof: Pre));
        Reject(() => new OperationReceipt("a", OperationOutcome.RolledBack, "rollback", Post));
        Reject(() => new OperationReceipt("a", OperationOutcome.Skipped));
        Reject(() => new ImageProof(ImageProofKind.Postimage, PlanTests.Artifact, ArtifactImage.Present(Hash), ArtifactImage.Present(Hash256.Compute([9]))));
    }

    public static void RecoveryReclassifiesSameOperations()
    {
        var initial = new ApplyReceipt(Hash, 1, ["a", "b"], [Result("a", OperationOutcome.Applied), Result("b", OperationOutcome.RecoveryRequired)], false);
        var restored = initial.Revise(2, [Result("a", OperationOutcome.RolledBack), Result("b", OperationOutcome.RolledBack)], true);
        Require(!restored.Success && restored.Requested == initial.Requested && restored.Counts[OperationOutcome.RolledBack] == 2
            && initial.Counts[OperationOutcome.Applied] == 1 && initial.Revision == 1, "新版回執重分類同集合，不能跨revision累加或改舊版");
        var completed = initial.Revise(2, [Result("a", OperationOutcome.Applied), Result("b", OperationOutcome.Applied)], true);
        Require(completed.Success && completed.PlanHash == initial.PlanHash, "recover確認postimage後可完成原apply");
        Reject(() => initial.Revise(1, restored.Results, true));
        Reject(() => initial.Revise(2, [Result("c", OperationOutcome.Applied)], true));
        Reject(() => restored.Revise(3, [Result("a", OperationOutcome.Applied), Result("b", OperationOutcome.Applied)], true));
        Require(restored.Revise(3, [Result("a", OperationOutcome.Conflict), Result("b", OperationOutcome.RecoveryRequired)], false).Requested == 2
            && restored.Counts[OperationOutcome.RolledBack] == 2, "部分rollback的已還原成員可在後續觀察轉Conflict或RecoveryRequired，原證據保留");
        var noop = new ApplyReceipt(Hash, 1, ["a"], [Result("a", OperationOutcome.Unchanged)], false);
        Require(noop.Revise(2, [Result("a", OperationOutcome.Conflict)], false).Counts[OperationOutcome.Conflict] == 1
            && noop.Revise(2, [Result("a", OperationOutcome.RecoveryRequired)], false).Counts[OperationOutcome.RecoveryRequired] == 1
            && noop.Counts[OperationOutcome.Unchanged] == 1, "後續真實內容改變或不可讀也能重分類unchanged，舊版證據不變");
        Reject(() => noop.Revise(2, [Result("a", OperationOutcome.Applied)], true));
        Reject(() => noop.Revise(2, [Result("a", OperationOutcome.RolledBack)], true));
    }
}
