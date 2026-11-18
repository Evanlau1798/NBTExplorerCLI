using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldApplyTests
{
    internal static WorldApplyEngine Engine(string root, string state, IArtifactStorage? storage = null)
        => new(new WorldInventory(root, World), storage ?? new ArtifactStorage(root, World),
            new FileSystemWorldBackupStore(Path.Combine(state, "objects")), new FileSystemApplyJournalStore(Path.Combine(state, "journals")));
    internal static PreparedWorldPlan Functions(string root, string state, IArtifactStorage storage, bool unchanged = false, bool mixed = false)
    {
        var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache"));
        var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(row => row.Address.Artifact.Kind == ArtifactKind.McFunction).Select(row => row.ToSlot("en_us", "zh_tw")).ToArray();
        Require(slots.Length == 3, "real scanner supplies the three standalone command slots");
        var operations = slots.Select((slot, index) => new LiteralPatch("literal-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", unchanged || mixed && slot.SourceText == "Text0" ? slot.SourceText : "譯:" + slot.SourceText))));
        var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
        return new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
    }
    public static void PureFunctionsApplyReadBackAndReplayOneReceipt()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var storage = new ArtifactStorage(root, World); using var prepared = Functions(root, state, storage); var engine = Engine(root, state, storage);
            Reject<ArgumentException>(() => engine.Apply(prepared, false));
            var receipt = engine.Apply(prepared, true);
            Require(receipt.Success && receipt.Requested == 3 && receipt.Counts[OperationOutcome.Applied] == 3, "a pure-text plan succeeds only when all promised physical outputs read back");
            for (var index = 0; index < 3; index++) Require(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(index)))) == "tellraw @a[name=Guard] \"譯:Text" + index + "\"\n", "applied function preserves selectors and framing");
            var replay = engine.Apply(prepared, true); var fresh = Engine(root, state).ReadReceipt(prepared.Plan.PlanHash)!;
            Require(ApplyReceiptJson.Write(receipt) == ApplyReceiptJson.Write(replay) && ApplyReceiptJson.Write(fresh) == ApplyReceiptJson.Write(receipt), "same plan resubmission and fresh process state return the same persisted receipt version");
            Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "successful apply releases all candidates and writer leases");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    internal sealed class FailReplace(int selected, bool after) : ArtifactFileIo
    {
        private int replaces;
        public override void Replace(string candidate, string target, bool absent)
        {
            var fail = ++replaces == selected;
            if (fail && !after) throw new IOException("Injected before world replace");
            base.Replace(candidate, target, absent);
            if (fail && after) throw new IOException("Injected after world replace");
        }
    }
    public static void InterruptedApplyRecoversOrPreservesThirdImages()
    {
        foreach (var conflict in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var storage = new ArtifactStorage(root, World, new FailReplace(2, after: true)); using var prepared = Functions(root, state, storage);
                var interrupted = Engine(root, state, storage).Apply(prepared, true);
                Require(!interrupted.Success && interrupted.Requested == 3 && interrupted.Counts[OperationOutcome.Applied] == 2
                    && interrupted.Counts[OperationOutcome.RecoveryRequired] == 1, "replace success before journal update is classified by actual pre/postimages, never called wholly unchanged");
                var path = Path.Combine(root, WorldQueryTests.Function(0)); if (conflict) File.WriteAllBytes(path, [9]);
                var recovered = Engine(root, state).Recover(prepared.Plan.PlanHash, true);
                Require(recovered.Revision > interrupted.Revision && recovered.Requested == 3 && recovered.Counts.Values.Sum() == 3, "recovery reclassifies the same operations into a new conserved receipt version");
                Require(conflict ? !recovered.Success && recovered.Counts[OperationOutcome.Conflict] == 1 && File.ReadAllBytes(path).SequenceEqual(new byte[] { 9 })
                    : recovered.Success && recovered.Counts[OperationOutcome.Applied] == 3, "recovery rolls forward known images and never overwrites an external third image");
                Require(ApplyReceiptJson.Write(Engine(root, state).ReadReceipt(prepared.Plan.PlanHash, interrupted.Revision)!) == ApplyReceiptJson.Write(interrupted), "older receipt versions remain retrievable without summing their counts");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void ExplicitRestoreAndOriginalRollbackHaveSeparateAccounting()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild(); var originalBackup = ScratchDirectory.CreateChild();
        try
        {
            var artifact = new ArtifactAddress(World, "a.dat", ArtifactKind.Nbt); var removed = new ArtifactAddress(World, "c.json", ArtifactKind.Json);
            Put(root, "a.dat", [10, 255]); var inventory = new WorldInventory(root, World);
            var backup = new WorldBackupService(inventory, new FileSystemWorldBackupStore(originalBackup)).Create([new(artifact, ArtifactImage.Present(Hash256.Compute(new byte[] { 10, 255 }))), new(removed, ArtifactImage.Absent)]);
            Put(root, "a.dat", [9]); Put(root, "c.json", [7]);
            var current = new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = ArtifactImage.Present(Hash256.Compute(new byte[] { 9 })), [removed] = ArtifactImage.Present(Hash256.Compute(new byte[] { 7 })) };
            var plan = new WorldPlanBuilder(inventory).Build(backup, [new("explicit-restore", new(backup.ManifestHash, backup.Manifest.Artifacts, current))]);
            var storage = new ArtifactStorage(root, World); using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(backup, plan, true);
            var applied = Engine(root, state, storage).Apply(prepared, true);
            Require(applied.Success && applied.Counts[OperationOutcome.Applied] == 1 && applied.Counts[OperationOutcome.RolledBack] == 0
                && File.ReadAllBytes(Path.Combine(root, "a.dat")).SequenceEqual(new byte[] { 10, 255 }) && !File.Exists(Path.Combine(root, "c.json")), "independent raw restore is applied, preserving corrupt backup bytes and target absence");
            var rolledBack = Engine(root, state).Recover(plan.PlanHash, true, rollback: true);
            Require(!rolledBack.Success && rolledBack.Counts[OperationOutcome.RolledBack] == 1
                && File.ReadAllBytes(Path.Combine(root, "a.dat")).SequenceEqual(new byte[] { 9 }) && File.ReadAllBytes(Path.Combine(root, "c.json")).SequenceEqual(new byte[] { 7 }), "only restoring this plan's verified raw preimages marks its own operation rolledBack");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); Directory.Delete(originalBackup, true); }
    }
    private sealed class RefuseTerminalPointer(IApplyJournalStore inner) : IApplyJournalStore
    {
        public byte[]? Read(Hash256 planHash) => inner.Read(planHash);
        public void Write(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            var journal = ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span));
            if (journal.Phase is ApplyJournalPhase.Completed or ApplyJournalPhase.RecoveryRequired) throw new IOException("Injected terminal pointer failure");
            inner.Write(planHash, bytes, expected);
        }
    }
    public static void FailedBackupDependencyCannotBecomeSuccessfulReadback()
    {
        BackupDependency(corrupt: false); BackupDependency(corrupt: true);
    }
    private static void BackupDependency(bool corrupt)
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var storage = new ArtifactStorage(root, World); using var prepared = Functions(root, state, storage);
            var objectRoot = Path.Combine(state, "objects"); var journalRoot = Path.Combine(state, "journals");
            var engine = new WorldApplyEngine(new WorldInventory(root, World), storage, new FileSystemWorldBackupStore(objectRoot), new RefuseTerminalPointer(new FileSystemApplyJournalStore(journalRoot)));
            Reject<IOException>(() => engine.Apply(prepared, true));
            var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(new FileSystemApplyJournalStore(journalRoot).Read(prepared.Plan.PlanHash)!));
            Require(record.Phase == ApplyJournalPhase.ReadBack && record.ReceiptHash is null, "all world outputs reached readback while receipt publication is still absent");
            var preimage = prepared.Plan.Preconditions.Single(input => input.Artifact.RelativePath == WorldQueryTests.Function(0)).Image.Hash!;
            var preimagePath = Path.Combine(objectRoot, "blob-" + preimage.Hex + ".bin");
            using var blocked = corrupt ? null : File.Open(preimagePath, FileMode.Open, FileAccess.Read, FileShare.None);
            if (corrupt) File.WriteAllBytes(preimagePath, [9]);
            var before = prepared.Plan.Outputs.ToDictionary(output => output.Artifact.RelativePath, output => File.ReadAllBytes(Path.Combine(root, output.Artifact.RelativePath)));
            var result = Engine(root, state).Recover(prepared.Plan.PlanHash, true);
            Require(!result.Success && !result.DependenciesComplete && before.All(pair => File.ReadAllBytes(Path.Combine(root, pair.Key)).SequenceEqual(pair.Value)),
                "verified current postimages cannot hide an unavailable or corrupt required preimage backup or authorize further writes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    private sealed class TamperAtBackup(IWorldBackupStore inner, Action tamper) : IWorldBackupStore
    {
        public byte[]? ReadBlob(Hash256 hash) => inner.ReadBlob(hash);
        public byte[]? ReadManifest(Hash256 hash) => inner.ReadManifest(hash);
        public void SaveBlob(Hash256 hash, ReadOnlyMemory<byte> bytes) => inner.SaveBlob(hash, bytes);
        public void SaveManifest(Hash256 hash, ReadOnlyMemory<byte> bytes) { inner.SaveManifest(hash, bytes); tamper(); }
    }
    public static void WholeCandidateSetIsCheckedBeforeTheFirstWorldCommit()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var storage = new ArtifactStorage(root, World); using var prepared = Functions(root, state, storage);
            var before = prepared.Plan.Preconditions.ToDictionary(input => input.Artifact.RelativePath, input => File.ReadAllBytes(Path.Combine(root, input.Artifact.RelativePath)));
            var objects = new TamperAtBackup(new FileSystemWorldBackupStore(Path.Combine(state, "objects")), () => File.WriteAllBytes(prepared.Manifest.Artifacts.Last().CandidatePath!, [9]));
            var engine = new WorldApplyEngine(new WorldInventory(root, World), storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals")));
            var result = engine.Apply(prepared, true);
            Require(!result.Success && result.Counts[OperationOutcome.Applied] == 0 && before.All(pair => File.ReadAllBytes(Path.Combine(root, pair.Key)).SequenceEqual(pair.Value)),
                "a candidate changed during durable backup publication must stop the complete set before the first world replacement");
            Require(Engine(root, state).Recover(prepared.Plan.PlanHash, true).Success, "explicit recovery can restage the previously verified immutable postimages without trusting the replaced candidate");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
}
