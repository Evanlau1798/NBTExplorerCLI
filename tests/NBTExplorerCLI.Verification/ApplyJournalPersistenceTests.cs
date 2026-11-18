using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class ApplyJournalPersistenceTests
{
    public static void ProgressPointersUseDurableCompareExchange()
    {
        var root = ScratchDirectory.CreateChild(); var foreign = ScratchDirectory.CreateChild();
        try
        {
            var plan = PlanTests.Plan(); var manifest = new PreparedManifest(plan, [PlanTests.Candidate()]); var binding = JournalBinding.From(manifest);
            var first = new ApplyJournal(binding, PlanTests.Preimage, 0, null, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null);
            var bytes = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(first)); var hash = Hash256.Compute(bytes);
            var store = new FileSystemApplyJournalStore(root); Require(store.Read(plan.PlanHash) is null, "a new owned journal store has no implicit plan completion");
            store.Write(plan.PlanHash, bytes, null);
            var fresh = new FileSystemApplyJournalStore(root);
            Require(fresh.Read(plan.PlanHash)!.SequenceEqual(bytes), "a fresh store observes the exact flushed journal pointer");
            Reject<ArtifactConflictException>(() => fresh.Write(plan.PlanHash, bytes, null));
            var committing = new ApplyJournal(binding, PlanTests.Preimage, 1, hash, ApplyDirection.Forward, ApplyJournalPhase.Committing, 1, null);
            var updated = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(committing)); store.Write(plan.PlanHash, updated, hash);
            Reject<ArtifactConflictException>(() => fresh.Write(plan.PlanHash, updated, hash));
            Require(ApplyJournalJson.Read(Encoding.UTF8.GetString(fresh.Read(plan.PlanHash)!)).Sequence == 1, "stale or duplicate writes cannot replace the current journal generation");
            Reject<ArgumentException>(() => store.Write(PlanTests.Preimage, updated, null));
            File.WriteAllText(Path.Combine(foreign, "user.txt"), "Keep"); Reject<IOException>(() => new FileSystemApplyJournalStore(foreign));
            Require(File.ReadAllText(Path.Combine(foreign, "user.txt")) == "Keep", "journal ownership never adopts or erases an unrelated state directory");
        }
        finally { Directory.Delete(root, true); Directory.Delete(foreign, true); }
    }
}
