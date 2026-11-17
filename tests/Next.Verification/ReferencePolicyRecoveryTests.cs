using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CliScanTests;

namespace Next.Verification;

internal static class ReferencePolicyRecoveryTests
{
    private sealed class StopPrepared(IApplyJournalStore inner) : IApplyJournalStore
    {
        public byte[]? Read(Hash256 hash) => inner.Read(hash);
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            inner.Write(hash, bytes, expected);
            if (ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span)).Phase == ApplyJournalPhase.Prepared)
                throw new InvalidOperationException("Controlled stop after the durable prepared pointer");
        }
    }
    private sealed record Frame(ApplyJournalHeader Legacy, Hash256 CurrentHash, Guid SessionId, long Revision, IWorldBackupStore Objects, IApplyJournalStore Journals);
    private static string State(string work) => Path.Combine(work, "state");
    private static WorldApplyEngine Engine(string world, Frame frame) => new(new WorldInventory(world, frame.Legacy.Plan.WorldId), new ArtifactStorage(world, frame.Legacy.Plan.WorldId), frame.Objects, frame.Journals);
    private static Hash256 Save(IWorldBackupStore objects, byte[] bytes) { var hash = Hash256.Compute(bytes); objects.SaveBlob(hash, bytes); return hash; }
    private static Frame PreparedLegacyFrame(string world, string work)
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
        var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(journals.Read(plan.PlanHash)!)); var current = ApplyJournalHeaderJson.Read(Encoding.UTF8.GetString(objects.ReadBlob(record.HeaderHash)!));
        var legacyPlan = new PatchPlan(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, "world-prepare-v1", plan.Operations, plan.Preconditions, plan.Outputs, plan.Dependencies, plan.Recovery);
        var manifest = new PreparedManifest(legacyPlan, current.Prepared.Artifacts); var legacy = new ApplyJournalHeader(legacyPlan, manifest, current.BackupManifestHash, current.AccessBinding);
        if (legacyPlan.PlanHash != plan.PlanHash)
        {
            var headerHash = Save(objects, Encoding.UTF8.GetBytes(ApplyJournalHeaderJson.Write(legacy)));
            var raw = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(new(JournalBinding.From(manifest), headerHash, 0, null, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null)));
            _ = Save(objects, raw); journals.Write(legacyPlan.PlanHash, raw, null);
        }
        return new(legacy, plan.PlanHash, id, revision, objects, journals);
    }
    private static void Materialize(string world, Frame frame, IEnumerable<PreparedArtifact> outputs)
    { foreach (var artifact in outputs) File.WriteAllBytes(Path.Combine(world, artifact.Artifact.RelativePath), frame.Objects.ReadBlob(artifact.Postimage.Hash!)!); }
    public static void LegacyMultipleGroupsCannotResumeForwardButRemainRollbackable()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var frame = PreparedLegacyFrame(world, work);
            var planFile = Path.Combine(work, "legacy-plan.json"); File.WriteAllText(planFile, PatchPlanJson.Write(frame.Legacy.Plan));
            var fresh = Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", Path.Combine(work, "fresh-state"));
            Require(fresh.Exit == 4 && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "an unjournaled legacy multiple-group plan requires regeneration before staging");
            Materialize(world, frame, frame.Legacy.Prepared.Artifacts.Take(1)); var partial = original.Keys.ToDictionary(path => path, File.ReadAllBytes); var before = frame.Journals.Read(frame.Legacy.Plan.PlanHash)!;
            var result = Run(work, "recover", world, "--plan-hash", frame.Legacy.Plan.PlanHash.Hex, "--world-offline", "--state-root", State(work));
            Require(result.Exit == 4 && result.Output.Contains("rollback", StringComparison.OrdinalIgnoreCase) && partial.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))
                && frame.Journals.Read(frame.Legacy.Plan.PlanHash)!.SequenceEqual(before), "legacy multiple-group forward policy must refuse without writes or fabricated progress; actual exit=" + result.Exit);
            var replay = Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", State(work));
            Require(replay.Exit == 4 && replay.Output.Contains("rollback-and-create-new-reference-plan", StringComparison.Ordinal)
                && partial.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && frame.Journals.Read(frame.Legacy.Plan.PlanHash)!.SequenceEqual(before), "apply replay enforces the same recovery policy without changing history");
            var rollback = Engine(world, frame).Recover(frame.Legacy.Plan.PlanHash, true, rollback: true);
            Require(rollback.DependenciesComplete && Engine(world, frame).ReadStatus(frame.Legacy.Plan.PlanHash)!.Direction == ApplyDirection.Rollback
                && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "verified raw rollback remains available for the legacy partial world");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void LegacyCompletedReceiptsRemainHistoryWithoutIssuingANewSeal()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedLegacyFrame(world, work); Materialize(world, frame, frame.Legacy.Prepared.Artifacts);
            var receipt = frame.Legacy.Prepared.Receipt(frame.Legacy.Plan, 0, frame.Legacy.Plan.Operations.Select(operation => new OperationReceipt(operation.Id, OperationOutcome.Applied, null,
                operation.RequiredOutputs.Select(artifact => { var image = frame.Legacy.Prepared.Artifacts.Single(output => output.Artifact == artifact).Postimage; return new ImageProof(ImageProofKind.Postimage, artifact, image, image); }))), true);
            var receiptHash = Save(frame.Objects, Encoding.UTF8.GetBytes(ApplyReceiptJson.Write(receipt))); var prior = frame.Journals.Read(frame.Legacy.Plan.PlanHash)!; var previousHash = Save(frame.Objects, prior);
            var record = ApplyJournalJson.Read(Encoding.UTF8.GetString(prior)); var completed = new ApplyJournal(record.Binding, record.HeaderHash, 1, previousHash, ApplyDirection.Forward, ApplyJournalPhase.Completed, frame.Legacy.Prepared.Artifacts.Length, receiptHash);
            var raw = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(completed)); _ = Save(frame.Objects, raw); frame.Journals.Write(frame.Legacy.Plan.PlanHash, raw, previousHash);
            Require(Engine(world, frame).Recover(frame.Legacy.Plan.PlanHash, true).Success && frame.Journals.Read(frame.Legacy.Plan.PlanHash)!.SequenceEqual(raw), "existing completed receipt remains immutable historical data");
            var store = new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions")); var before = store.Read(frame.SessionId)!;
            var result = Run(work, "translate", "seal", world, "--session-id", frame.SessionId.ToString(), "--revision", frame.Revision.ToString(), "--operation-id", Guid.NewGuid().ToString(),
                "--plan-hash", frame.Legacy.Plan.PlanHash.Hex, "--state-root", State(work));
            Require(result.Exit == 4 && store.Read(frame.SessionId)!.SequenceEqual(before), "a legacy multiple-group receipt cannot authorize a new completion under the current policy; actual exit=" + result.Exit);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void CurrentMultipleGroupsRecoverAndSealWithTheirVerifiedPolicy()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var frame = PreparedLegacyFrame(world, work);
            Require(frame.CurrentHash != frame.Legacy.Plan.PlanHash, "fresh multiple-group plans carry a policy distinct from legacy v1");
            Require(Engine(world, frame).Recover(frame.CurrentHash, true).Success, "the current policy supports normal prepared-pointer recovery");
            var result = Run(work, "translate", "seal", world, "--session-id", frame.SessionId.ToString(), "--revision", frame.Revision.ToString(), "--operation-id", Guid.NewGuid().ToString(),
                "--plan-hash", frame.CurrentHash.Hex, "--state-root", State(work));
            using var completed = Success(result);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
