using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class ReferencePolicyRecoveryTests
{
    public static void ReplayAndStatusReadFailuresRetainExistingPartialJournalIdentity()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedPolicyFrame(world, work); Materialize(world, frame, frame.Unapproved.Prepared.Artifacts.Take(1));
            var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            foreach (var name in new[] { "TryReplay", "Status" })
            {
                var engine = new WorldApplyEngine(new WorldInventory(world, frame.Unapproved.Plan.WorldId), new ArtifactStorage(world, frame.Unapproved.Plan.WorldId), frame.Objects, new RefuseRead());
                var method = typeof(NBTExplorerCLI.Cli.CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.WriteExecution")!.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                object result;
                try { result = method.Invoke(null, [engine, name == "TryReplay" ? (object)frame.Unapproved.Plan : frame.Unapproved.Plan.PlanHash, world, State(work)])!; }
                catch (System.Reflection.TargetInvocationException failure) { throw new InvalidOperationException("initial " + name + " read failure for a partial journal must preserve recovery context", failure.InnerException); }
                Require((int)result.GetType().GetProperty("Exit")!.GetValue(result)! == 7, "a prior partial journal cannot be hidden by an unreadable initial pointer");
                using var output = System.Text.Json.JsonDocument.Parse(CommandOutputJson.Write("apply", result.GetType().GetProperty("Data")!.GetValue(result))); var data = output.RootElement.GetProperty("data");
                Require(data.GetProperty("planHash").GetString() == frame.Unapproved.Plan.PlanHash.Hex && data.GetProperty("recovery").GetProperty("world").GetString() == Path.GetFullPath(world)
                    && data.GetProperty("recovery").GetProperty("stateRoot").GetString() == Path.GetFullPath(State(work)), "both replay and status retain their parsed recovery identity");
            }
            Require(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "read failures do not modify the existing partial world");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private sealed class RefuseRead : IApplyJournalStore
    {
        public byte[]? Read(Hash256 hash) => throw new IOException("Injected initial journal lookup failure");
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected) => throw new InvalidOperationException("Unreadable replay cannot write");
    }
    public static void PolicyRefusalStatusIoKeepsRecoveryIdentity()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedPolicyFrame(world, work); Materialize(world, frame, frame.Unapproved.Prepared.Artifacts.Take(1));
            var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var store = new FailPolicyStatus(frame.Journals);
            var engine = new WorldApplyEngine(new WorldInventory(world, frame.Unapproved.Plan.WorldId), new ArtifactStorage(world, frame.Unapproved.Plan.WorldId), frame.Objects, store);
            var method = typeof(NBTExplorerCLI.Cli.CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.WriteExecution")!.GetMethod("Recover", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            object result;
            try { result = method.Invoke(null, [engine, frame.Unapproved.Plan.PlanHash, world, State(work), false])!; }
            catch (System.Reflection.TargetInvocationException failure) { throw new InvalidOperationException("policy refusal followed by failed status must preserve recovery identity", failure.InnerException); }
            Require((int)result.GetType().GetProperty("Exit")!.GetValue(result)! == 7 && store.Reads == 2, "controlled fault follows the actual policy refusal, not the first journal read");
            using var output = System.Text.Json.JsonDocument.Parse(CommandOutputJson.Write("recover", result.GetType().GetProperty("Data")!.GetValue(result))); var data = output.RootElement.GetProperty("data");
            Require(data.GetProperty("planHash").GetString() == frame.Unapproved.Plan.PlanHash.Hex && data.GetProperty("recovery").GetProperty("world").GetString() == Path.GetFullPath(world)
                && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "policy/status failure retains exact context and writes no world content");
            Require(data.GetProperty("policyRepair").GetString() == "rollback-and-create-new-reference-plan"
                && data.GetProperty("recovery").GetProperty("repair").GetString() == data.GetProperty("policyRepair").GetString(), "policy refusal has one consistent rollback/replan repair across both projections");
            var lease = (IArtifactStorage)new ArtifactStorage(world, frame.Unapproved.Plan.WorldId);
            using (var pending = lease.AcquirePendingWrite())
                Require(pending.Current?.PlanHash == frame.Unapproved.Plan.PlanHash, "recovering a markerless journal must register it before policy refusal");
            Require(Run(work, "recover", world, "--plan-hash", frame.Unapproved.Plan.PlanHash.Hex, "--state-root", State(work), "--world-offline", "--rollback").Exit == 8, "the retained identity still supports an explicit real CLI rollback after removing the read fault");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private sealed class FailPolicyStatus(IApplyJournalStore inner) : IApplyJournalStore
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public int Reads { get; private set; }
        public byte[]? Read(Hash256 hash) => ++Reads == 1 ? inner.Read(hash) : throw new IOException("Injected status read after reference policy refusal");
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected) => throw new InvalidOperationException("Policy refusal must not publish journal changes");
    }
    private sealed class StopPrepared(IApplyJournalStore inner) : IApplyJournalStore
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public byte[]? Read(Hash256 hash) => inner.Read(hash);
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            inner.Write(hash, bytes, expected);
            if (ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span)).Phase == ApplyJournalPhase.Prepared)
                throw new InvalidOperationException("Controlled stop after the durable prepared pointer");
        }
    }
    private sealed record Frame(ApplyJournalHeader Unapproved, Hash256 CurrentHash, Guid SessionId, long Revision, IWorldBackupStore Objects, IApplyJournalStore Journals);
    private static string State(string work) => Path.Combine(work, "state");
    private static WorldApplyEngine Engine(string world, Frame frame) => new(new WorldInventory(world, frame.Unapproved.Plan.WorldId), new ArtifactStorage(world, frame.Unapproved.Plan.WorldId), frame.Objects, frame.Journals);
    private static Hash256 Save(IWorldBackupStore objects, byte[] bytes) { var hash = Hash256.Compute(bytes); objects.SaveBlob(hash, bytes); return hash; }
    private static Frame PreparedPolicyFrame(string world, string work)
    {
        var (id, revision) = CliReferencePlanTests.Session(world, work, bothTypes: true);
        var service = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions"))); var state = service.Read(id);
        var inventory = new WorldInventory(world, state.Source.WorldId); var storage = new ArtifactStorage(world, state.Source.WorldId);
        var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache"))).QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
        var names = state.Slots.Where(slot => slot.State == SessionSlotState.Translated).Select(slot => new LiteralTranslation(slot.Slot, slot.Reply!));
        var groups = new WorldReferenceService(inventory).CreateGroups(snapshot, "policy", names); Require(groups.Length == 2, "control: two individually safe typed groups form the fixture");
        var plan = new WorldPlanBuilder(inventory).Build(snapshot, groups); var objects = new FileSystemWorldBackupStore(Path.Combine(State(work), "objects")); var journals = new FileSystemApplyJournalStore(Path.Combine(State(work), "journals"));
        using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
        CandidateIoTests.Reject<InvalidOperationException>(() => new WorldApplyEngine(inventory, storage, objects, new StopPrepared(journals)).Apply(prepared, true));
        // Construct an older journal without a world marker to exercise its recovery policy directly.
        using (var pending = ((IArtifactStorage)storage).AcquirePendingWrite()) pending.Complete();
        var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(journals.Read(plan.PlanHash)!)); var current = ApplyJournalHeaderJson.Read(Encoding.UTF8.GetString(objects.ReadBlob(record.HeaderHash)!));
        var unapprovedPlan = new PatchPlan(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, "unapproved-group-policy", plan.Operations, plan.Preconditions, plan.Outputs, plan.Dependencies, plan.Recovery);
        var manifest = new PreparedManifest(unapprovedPlan, current.Prepared.Artifacts); var unapproved = new ApplyJournalHeader(unapprovedPlan, manifest, current.BackupManifestHash, current.AccessBinding);
        if (unapprovedPlan.PlanHash != plan.PlanHash)
        {
            var headerHash = Save(objects, Encoding.UTF8.GetBytes(ApplyJournalHeaderJson.Write(unapproved)));
            var raw = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(new(JournalBinding.From(manifest), headerHash, 0, null, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null)));
            _ = Save(objects, raw); journals.Write(unapprovedPlan.PlanHash, raw, null);
        }
        return new(unapproved, plan.PlanHash, id, revision, objects, journals);
    }
    private static void Materialize(string world, Frame frame, IEnumerable<PreparedArtifact> outputs)
    { foreach (var artifact in outputs) File.WriteAllBytes(Path.Combine(world, artifact.Artifact.RelativePath), frame.Objects.ReadBlob(artifact.Postimage.Hash!)!); }
    public static void UnapprovedGroupsRefuseForwardAndPermitVerifiedRollback()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var frame = PreparedPolicyFrame(world, work);
            var planFile = Path.Combine(work, "unapproved-plan.json"); File.WriteAllText(planFile, PatchPlanJson.Write(frame.Unapproved.Plan));
            var fresh = Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", Path.Combine(work, "fresh-state"));
            Require(fresh.Exit == 4 && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "an unjournaled unapproved multiple-group plan requires regeneration before staging");
            Materialize(world, frame, frame.Unapproved.Prepared.Artifacts.Take(1)); var partial = original.Keys.ToDictionary(path => path, File.ReadAllBytes); var before = frame.Journals.Read(frame.Unapproved.Plan.PlanHash)!;
            var result = Run(work, "recover", world, "--plan-hash", frame.Unapproved.Plan.PlanHash.Hex, "--world-offline", "--state-root", State(work));
            Require(result.Exit == 4 && result.Output.Contains("rollback", StringComparison.OrdinalIgnoreCase) && partial.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))
                && frame.Journals.Read(frame.Unapproved.Plan.PlanHash)!.SequenceEqual(before), "unapproved multiple-group forward policy must refuse without writes or fabricated progress; actual exit=" + result.Exit);
            var replay = Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", State(work));
            Require(replay.Exit == 4 && replay.Output.Contains("rollback-and-create-new-reference-plan", StringComparison.Ordinal)
                && partial.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && frame.Journals.Read(frame.Unapproved.Plan.PlanHash)!.SequenceEqual(before), "apply replay enforces the same recovery policy without changing history");
            var rollback = Engine(world, frame).Recover(frame.Unapproved.Plan.PlanHash, true, rollback: true);
            Require(rollback.DependenciesComplete && Engine(world, frame).ReadStatus(frame.Unapproved.Plan.PlanHash)!.Direction == ApplyDirection.Rollback
                && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "verified raw rollback remains available for the unapproved partial world");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void UnapprovedGroupsCannotAuthorizeSessionSeal()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedPolicyFrame(world, work); Materialize(world, frame, frame.Unapproved.Prepared.Artifacts);
            var receipt = frame.Unapproved.Prepared.Receipt(frame.Unapproved.Plan, 0, frame.Unapproved.Plan.Operations.Select(operation => new OperationReceipt(operation.Id, OperationOutcome.Applied, null,
                operation.RequiredOutputs.Select(artifact => { var image = frame.Unapproved.Prepared.Artifacts.Single(output => output.Artifact == artifact).Postimage; return new ImageProof(ImageProofKind.Postimage, artifact, image, image); }))), true);
            var receiptHash = Save(frame.Objects, Encoding.UTF8.GetBytes(ApplyReceiptJson.Write(receipt))); var prior = frame.Journals.Read(frame.Unapproved.Plan.PlanHash)!; var previousHash = Save(frame.Objects, prior);
            var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(prior)); var completed = new ApplyJournal(record.Binding, record.HeaderHash, 1, previousHash, ApplyDirection.Forward, ApplyJournalPhase.Completed, frame.Unapproved.Prepared.Artifacts.Length, receiptHash);
            var raw = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(completed)); _ = Save(frame.Objects, raw); frame.Journals.Write(frame.Unapproved.Plan.PlanHash, raw, previousHash);
            Require(Engine(world, frame).Recover(frame.Unapproved.Plan.PlanHash, true).Success && frame.Journals.Read(frame.Unapproved.Plan.PlanHash)!.SequenceEqual(raw), "existing completed receipt remains immutable historical data");
            var store = new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions")); var before = store.Read(frame.SessionId)!;
            var result = Run(work, "translate", "seal", world, "--session-id", frame.SessionId.ToString(), "--revision", frame.Revision.ToString(), "--operation-id", Guid.NewGuid().ToString(),
                "--plan-hash", frame.Unapproved.Plan.PlanHash.Hex, "--state-root", State(work));
            Require(result.Exit == 4 && store.Read(frame.SessionId)!.SequenceEqual(before), "a unapproved multiple-group receipt cannot authorize a new completion under the current policy; actual exit=" + result.Exit);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void CurrentMultipleGroupsRecoverAndSealWithTheirVerifiedPolicy()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedPolicyFrame(world, work);
            Require(frame.CurrentHash != frame.Unapproved.Plan.PlanHash, "fresh multiple-group plans carry a policy distinct from an unapproved policy");
            Require(Engine(world, frame).Recover(frame.CurrentHash, true).Success, "the current policy supports normal prepared-pointer recovery");
            var result = Run(work, "translate", "seal", world, "--session-id", frame.SessionId.ToString(), "--revision", frame.Revision.ToString(), "--operation-id", Guid.NewGuid().ToString(),
                "--plan-hash", frame.CurrentHash.Hex, "--state-root", State(work));
            using var completed = Success(result);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
