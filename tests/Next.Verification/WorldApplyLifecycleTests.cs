using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class WorldApplyLifecycleTests
{
    private sealed class CancelAfterReplace(CancellationTokenSource cancellation) : ArtifactFileIo
    {
        public override void Replace(string candidate, string target, bool absent)
        { base.Replace(candidate, target, absent); cancellation.Cancel(); }
    }
    public static void NoopAndCancellationKeepOriginalsAndCounts()
    {
        foreach (var mode in new[] { "noop", "before", "after" })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                using var cancellation = new CancellationTokenSource();
                var storage = mode == "after" ? new ArtifactStorage(root, World, new CancelAfterReplace(cancellation)) : new ArtifactStorage(root, World);
                using var prepared = WorldApplyTests.Functions(root, state, storage, unchanged: mode == "noop");
                var originals = prepared.Plan.Outputs.ToDictionary(output => output.Artifact.RelativePath, output => File.ReadAllBytes(Path.Combine(root, output.Artifact.RelativePath)));
                if (mode == "before") cancellation.Cancel();
                var receipt = WorldApplyTests.Engine(root, state, storage).Apply(prepared, true, cancellation.Token);
                Require(receipt.Requested == 3 && receipt.Counts.Values.Sum() == 3, "no-op and cancellation keep the original operation set and exclusive outcome counts");
                if (mode == "noop") Require(receipt.Success && receipt.Counts[OperationOutcome.Unchanged] == 3, "unchanged postimages read back without physical replacements");
                else if (mode == "before") Require(!receipt.Success && receipt.Counts[OperationOutcome.Cancelled] == 3
                    && ApplyReceiptJson.Write(WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true)) == ApplyReceiptJson.Write(receipt), "cancellation before first commit is durable and replays its terminal receipt");
                else Require(!receipt.Success && receipt.Counts[OperationOutcome.Applied] == 1 && receipt.Counts[OperationOutcome.RecoveryRequired] == 2
                    && WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true).Success, "cancellation after a replacement requires safe recovery of the remaining operations");
                if (mode != "after") Require(originals.All(pair => File.ReadAllBytes(Path.Combine(root, pair.Key)).SequenceEqual(pair.Value)), "no-op and early cancellation preserve all original function bytes");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void AnUnchangedMemberCanLaterReportARealConflict()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var storage = new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(1, after: true));
            using var prepared = WorldApplyTests.Functions(root, state, storage, mixed: true);
            var first = WorldApplyTests.Engine(root, state, storage).Apply(prepared, true);
            Require(first.Counts[OperationOutcome.Unchanged] == 1 && first.Counts[OperationOutcome.Applied] == 1 && first.Counts[OperationOutcome.RecoveryRequired] == 1,
                "real partial apply includes a previously verified unchanged operation");
            var path = Path.Combine(root, WorldQueryTests.Function(0)); File.WriteAllBytes(path, [9]);
            var before = File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(2)));
            var next = WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true);
            Require(!next.Success && next.Revision > first.Revision && next.Counts[OperationOutcome.Conflict] == 1 && next.Requested == 3
                && File.ReadAllBytes(path).SequenceEqual(new byte[] { 9 }) && File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(2))).SequenceEqual(before),
                "new external content reclassifies the same unchanged member as conflict without changing it or starting more world commits");
            Require(WorldApplyTests.Engine(root, state).ReadReceipt(prepared.Plan.PlanHash, first.Revision)!.Counts[OperationOutcome.Unchanged] == 1, "the older unchanged evidence remains immutable");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void APartiallyRolledBackMemberCanReportNewContentOrUnreadability()
    {
        foreach (var locked in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var storage = new ArtifactStorage(root, World); using var prepared = WorldApplyTests.Functions(root, state, storage);
                Require(WorldApplyTests.Engine(root, state, storage).Apply(prepared, true).Success, "rollback control starts after a complete apply");
                var broken = new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(1, after: true));
                var first = WorldApplyTests.Engine(root, state, broken).Recover(prepared.Plan.PlanHash, true, rollback: true);
                Require(first.Counts[OperationOutcome.RolledBack] == 1 && first.Counts[OperationOutcome.RecoveryRequired] == 2, "partial rollback has one verified restored member before the next observation");
                var path = Path.Combine(root, WorldQueryTests.Function(0)); if (!locked) File.WriteAllBytes(path, [9]);
                var before = File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(2)));
                using var block = locked ? File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                var next = WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true); block?.Dispose();
                Require(!next.Success && next.Revision > first.Revision && next.Requested == 3
                    && (locked ? next.Counts[OperationOutcome.RecoveryRequired] == 3 : next.Counts[OperationOutcome.Conflict] == 1 && File.ReadAllBytes(path).SequenceEqual(new byte[] { 9 }))
                    && File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(2))).SequenceEqual(before), "partial rollback emits a new conserved Conflict/RecoveryRequired receipt without overwriting unknown contents or starting more restores");
                Require(WorldApplyTests.Engine(root, state).ReadReceipt(prepared.Plan.PlanHash, first.Revision)!.Counts[OperationOutcome.RolledBack] == 1, "old rolledBack evidence remains unchanged");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
}
