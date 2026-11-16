using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class WorldApplyIntegrityTests
{
    public static void DamagedObjectsAndMismatchedBindingsCannotAuthorizeWrites()
    {
        foreach (var mode in new[] { "postimage", "header", "binding", "foreign" })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var storage = new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(1, after: true));
                using var prepared = WorldApplyTests.Functions(root, state, storage);
                var interrupted = WorldApplyTests.Engine(root, state, storage).Apply(prepared, true);
                Require(!interrupted.Success && interrupted.Counts[OperationOutcome.Applied] == 1, "integrity controls begin with one known committed output and two pending outputs");
                var original = prepared.Plan.Outputs.ToDictionary(output => output.Artifact.RelativePath, output => File.ReadAllBytes(Path.Combine(root, output.Artifact.RelativePath)));
                var objectRoot = Path.Combine(state, "objects"); var store = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
                var bytes = store.Read(prepared.Plan.PlanHash)!; var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes));
                if (mode == "postimage")
                {
                    var hash = prepared.Manifest.Artifacts.Last().Postimage.Hash!;
                    File.WriteAllBytes(Path.Combine(objectRoot, "blob-" + hash.Hex + ".bin"), [9]);
                    var receipt = WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true);
                    Require(!receipt.Success && !receipt.DependenciesComplete && receipt.Requested == 3, "a damaged pending postimage cannot be promoted to complete recovery");
                }
                else if (mode == "header")
                {
                    File.WriteAllBytes(Path.Combine(objectRoot, "blob-" + record.HeaderHash.Hex + ".bin"), [9]);
                    Reject<InvalidDataException>(() => WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true));
                }
                else if (mode == "binding")
                {
                    var mismatched = new ApplyJournal(new(record.Binding.PlanHash, PlanTests.Postimage), record.HeaderHash, record.Sequence + 1,
                        Hash256.Compute(bytes), record.Direction, ApplyJournalPhase.Committing, record.CompletedCount, record.ReceiptHash);
                    var changed = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(mismatched)); var objects = new FileSystemWorldBackupStore(objectRoot);
                    objects.SaveBlob(Hash256.Compute(changed), changed); store.Write(prepared.Plan.PlanHash, changed, Hash256.Compute(bytes));
                    Reject<InvalidDataException>(() => WorldApplyTests.Engine(root, state).Recover(prepared.Plan.PlanHash, true));
                }
                else
                {
                    var foreignWorld = Guid.NewGuid(); var engine = new WorldApplyEngine(new WorldInventory(root, foreignWorld), new ArtifactStorage(root, foreignWorld),
                        new FileSystemWorldBackupStore(objectRoot), store);
                    Reject<InvalidDataException>(() => engine.Recover(prepared.Plan.PlanHash, true));
                }
                Require(original.All(pair => File.ReadAllBytes(Path.Combine(root, pair.Key)).SequenceEqual(pair.Value)), "all corruption, binding and world failures preserve every current artifact byte");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
}
