using System.Reflection;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class CliWriteRecoveryTests
{
    public static void NewPlanWaitsForAnInterruptedWorldWrite()
    {
        foreach (var interruption in new[] { "replace", "journal-publication", "external-conflict", "markerless-conflict", "markerless-replay" })
            VerifyWriteOrder(interruption);
    }

    private static void VerifyWriteOrder(string interruption)
    {
        var world = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild(); var other = ScratchDirectory.CreateChild();
        try
        {
            var first = Path.Combine(state, "first.json"); var second = Path.Combine(state, "second.json");
            using var previewA = Success(Run(state, "convert", world, "--path", WorldQueryTests.Function(0), "--lang", "en_us", "--state-root", state, "--output", first));
            using var previewB = Success(Run(state, "convert", world, "--path", WorldQueryTests.Function(2), "--lang", "en_us", "--language-output", "second.zip#assets/test/lang/en_us.json", "--state-root", state, "--output", second));
            var plan = PatchPlanJson.Read(File.ReadAllText(first)); var inventory = new WorldInventory(world, plan.WorldId);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                .QueryAsync(new(new(SourceIdentity.ReadSnapshotScope(plan.SnapshotId)))).GetAwaiter().GetResult().Snapshot!;
            var storage = new ArtifactStorage(world, plan.WorldId, new WorldApplyTests.FailReplace(1, after: true));
            var initialFile = Path.Combine(world, WorldQueryTests.Function(0)); var initialBytes = File.ReadAllBytes(initialFile);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
            var engine = new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")),
                interruption == "journal-publication" ? new FailInitialJournalWrite(journals) : journals);
            if (interruption == "journal-publication")
            {
                CandidateIoTests.Reject<IOException>(() => engine.Apply(prepared, true));
                Require(engine.ReadStatus(plan.PlanHash) is null && File.ReadAllBytes(initialFile).SequenceEqual(initialBytes),
                    "an interrupted pointer publication leaves only the pending marker and its immutable anchor");
            }
            else
            {
                var receipt = engine.Apply(prepared, true);
                Require(!receipt.Success && engine.ReadStatus(plan.PlanHash)?.Phase == ApplyJournalPhase.RecoveryRequired
                    && !File.ReadAllBytes(initialFile).SequenceEqual(initialBytes), "the interrupted plan has changed an actual world file");
            }
            var output = Path.Combine(world, "second.zip"); var file = Path.Combine(world, WorldQueryTests.Function(2));
            var before = File.ReadAllBytes(file);
            string[] apply = ["apply", world, "--plan", second, "--world-offline"];
            string[] recover = ["recover", world, "--plan-hash", plan.PlanHash.Hex, "--world-offline"];
            foreach (var selected in new[] { state, other })
            {
                var refused = Run(state, [.. apply, "--state-root", selected]);
                using var json = JsonDocument.Parse(refused.Output);
                Require(refused.Exit == 7 && json.RootElement.GetProperty("data").TryGetProperty("pendingPlanHash", out var pending)
                    && pending.GetString() == plan.PlanHash.Hex && !File.Exists(output) && File.ReadAllBytes(file).SequenceEqual(before),
                    $"new plan must identify the pending plan and preserve its own outputs; {interruption}: exit {refused.Exit}, {refused.Output}");
            }
            Require(Run(state, [.. recover, "--state-root", other]).Exit == 7, "the pending plan cannot be recovered with a different physical state store");
            if (interruption == "markerless-replay")
            {
                using (var pending = ((IArtifactStorage)storage).AcquirePendingWrite()) pending.Complete();
                File.WriteAllBytes(initialFile, initialBytes);
                using var retry = new WorldPlanPreparation(inventory, new ArtifactStorage(world, plan.WorldId)).Prepare(snapshot, plan, true);
                var recoveryStorage = new ObserveRecoveryLock(new ArtifactStorage(world, plan.WorldId), () =>
                {
                    var duringHandoff = Run(state, [.. apply, "--state-root", other]);
                    Require(duringHandoff.Exit == 7 && !File.Exists(output) && File.ReadAllBytes(file).SequenceEqual(before),
                        $"the adopted marker must protect the lock handoff before replay reacquires it; apply exit {duringHandoff.Exit}");
                });
                var restarted = new WorldApplyEngine(inventory, recoveryStorage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")), journals);
                Require(restarted.Apply(retry, true).Success, "the interrupted plan still finishes through replay after releasing the preparation lease");
            }
            else if (interruption is "external-conflict" or "markerless-conflict")
            {
                File.WriteAllBytes(initialFile, [9]);
                var conflict = Run(state, [.. recover, "--state-root", state, "--rollback"]);
                Require(conflict.Exit == 3 && File.ReadAllBytes(initialFile).SequenceEqual(new byte[] { 9 })
                    && Run(state, [.. apply, "--state-root", state]).Exit == 7, "a third image remains intact and cannot release the world for new writes");
                if (interruption == "markerless-conflict")
                {
                    // Model an existing Conflict journal written before world markers were introduced.
                    using (var pending = ((IArtifactStorage)storage).AcquirePendingWrite()) pending.Complete();
                    var older = Run(state, [.. recover, "--state-root", state, "--rollback"]);
                    var attempt = Run(state, [.. apply, "--state-root", other]);
                    Require(older.Exit == 3 && attempt.Exit == 7 && File.ReadAllBytes(initialFile).SequenceEqual(new byte[] { 9 })
                        && !File.Exists(output) && File.ReadAllBytes(file).SequenceEqual(before),
                        $"recovering a markerless Conflict preserves files and prevents a new plan; recovery exit {older.Exit}, apply exit {attempt.Exit}");
                }
                File.WriteAllBytes(initialFile, initialBytes);
                var resolved = Run(state, [.. recover, "--state-root", state, "--rollback"]);
                using var result = JsonDocument.Parse(resolved.Output);
                Require(resolved.Exit == 3 && result.RootElement.GetProperty("data").GetProperty("phase").GetString() == "completed",
                    $"all verified preimages may release the pending marker without rewriting the conflict receipt; {resolved.Output}");
            }
            else
            {
                using var recovered = Success(Run(state, [.. recover, "--state-root", state]));
            }
            using var applied = Success(Run(state, [.. apply, "--state-root", other]));
            Require(File.Exists(output) && !File.ReadAllBytes(file).SequenceEqual(before), "the new plan can write after the first recovery is settled");
        }
        finally { Directory.Delete(world, true); Directory.Delete(state, true); Directory.Delete(other, true); }
    }

    private sealed class FailInitialJournalWrite(IApplyJournalStore inner) : IApplyJournalStore
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public byte[]? Read(Hash256 hash) => inner.Read(hash);
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected) => throw new IOException("Interrupted before the first journal pointer publication");
    }

    private sealed class ObserveRecoveryLock(IArtifactStorage inner, Action beforeAcquire) : IArtifactStorage
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public IPendingWorldWrite AcquirePendingWrite() { beforeAcquire(); return inner.AcquirePendingWrite(); }
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes) => inner.Stage(artifact, preimage, bytes);
        public IArtifactCandidate StageTracked(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, bool delete)
            => inner.StageTracked(artifact, preimage, bytes, delete);
    }

    public static void RealCandidateCleanupFailuresKeepRecoveryIdentityAndSavedReceipt()
    {
        var world = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild(); var locks = new List<FileStream>();
        try
        {
            var file = Path.Combine(state, "plan.json");
            using var preview = Success(Run(state, "convert", world, "--lang", "en_us", "--state-root", state, "--output", file));
            var plan = PatchPlanJson.Read(File.ReadAllText(file)); var inventory = new WorldInventory(world, plan.WorldId);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                .QueryAsync(new(new(SourceIdentity.ReadSnapshotScope(plan.SnapshotId)))).GetAwaiter().GetResult().Snapshot!;
            var io = new CleanupIo(); var storage = new ArtifactStorage(world, plan.WorldId, io);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            io.AfterFirst = () => { foreach (var artifact in prepared.Manifest.Artifacts.Where(item => File.Exists(item.CandidatePath))) locks.Add(File.Open(artifact.CandidatePath!, FileMode.Open, FileAccess.Read, FileShare.None)); };
            var engine = new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")), new FileSystemApplyJournalStore(Path.Combine(state, "journals")));
            var method = typeof(NBTExplorerCLI.Cli.CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.WriteCommands")!.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!;
            object result;
            try { result = method.Invoke(null, [prepared, engine, world, state])!; }
            catch (TargetInvocationException failure) { throw new InvalidOperationException("real File.Delete cleanup failures after partial commit must retain recovery identity", failure.InnerException); }
            Require(locks.Count > 0 && (int)result.GetType().GetProperty("Exit")!.GetValue(result)! == 7, "controlled partial replace reaches locked, unattempted native candidates");
            using var output = JsonDocument.Parse(CommandOutputJson.Write("convert", result.GetType().GetProperty("Data")!.GetValue(result))); var data = output.RootElement.GetProperty("data");
            Require(data.GetProperty("planHash").GetString() == plan.PlanHash.Hex && data.GetProperty("recovery").GetProperty("stateRoot").GetString() == Path.GetFullPath(state), "cleanup errors preserve actual recovery identity");
            var saved = ApplyReceiptJson.Read(data.GetProperty("receipt").GetRawText(), plan, prepared.Manifest);
            var first = plan.Preconditions.Single(input => input.Artifact.RelativePath == WorldQueryTests.Function(0));
            Require(!saved.Success && saved.Counts.Values.Sum() == saved.Requested && saved.Counts[OperationOutcome.RecoveryRequired] > 0
                && Hash256.Compute(File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0)))) != first.Image.Hash,
                "the persisted partial receipt survives cleanup errors; key operations still require their pending language dependency despite a committed component");
            foreach (var lease in locks) lease.Dispose(); locks.Clear();
            using var recovered = Success(Run(state, "recover", world, "--plan-hash", plan.PlanHash.Hex, "--state-root", state, "--world-offline"));
        }
        finally { foreach (var lease in locks) lease.Dispose(); Directory.Delete(world, true); Directory.Delete(state, true); }
    }
    private sealed class CleanupIo : ArtifactFileIo
    {
        internal Action? AfterFirst { get; set; }
        private bool first = true;
        public override void Replace(string candidate, string target, bool absent)
        {
            base.Replace(candidate, target, absent);
            if (!first) return;
            first = false; AfterFirst!(); throw new IOException("Injected interruption after first physical replacement");
        }
    }
    public static void MissingJournalKeepsItsNotFoundClassification()
    {
        var world = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var hash = Hash256.Compute("never-published"u8); var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            foreach (var command in new[] { "status", "recover" })
            {
                var result = Run(state, [command, world, "--state-root", state, "--plan-hash", hash.Hex, .. command == "recover" ? new[] { "--world-offline" } : Array.Empty<string>()]);
                using var output = JsonDocument.Parse(result.Output);
                Require(result.Exit == 5 && output.RootElement.GetProperty("error").GetProperty("code").GetString() == "FILE_NOT_FOUND", "an actually absent journal retains the original not-found classification: " + result.Output);
                if (command == "recover") Require(output.RootElement.GetProperty("data").GetProperty("planHash").GetString() == hash.Hex, "missing journal errors retain the supplied exact recovery identity");
            }
            Require(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "no journal authorizes no world write");
        }
        finally { Directory.Delete(world, true); Directory.Delete(state, true); }
    }
    public static void PostcommitJournalFailuresKeepRecoveryIdentityAndHonestAccounting()
    {
        foreach (var mode in new[] { "terminal", "terminal-missing", "status" })
        {
            var world = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var file = Path.Combine(state, "plan.json");
                using var preview = Success(Run(state, "convert", world, "--lang", "en_us", "--state-root", state, "--output", file));
                var plan = PatchPlanJson.Read(File.ReadAllText(file)); var inventory = new WorldInventory(world, plan.WorldId);
                var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                    .QueryAsync(new(new(SourceIdentity.ReadSnapshotScope(plan.SnapshotId)))).GetAwaiter().GetResult().Snapshot!;
                var inner = new FileSystemApplyJournalStore(Path.Combine(state, "journals")); var journals = new FaultJournal(inner, mode);
                var storage = new ArtifactStorage(world, plan.WorldId); using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
                var engine = new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")), journals);
                var method = typeof(NBTExplorerCLI.Cli.CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.WriteCommands")!.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!;
                object result;
                try { result = method.Invoke(null, [prepared, engine, world, state])!; }
                catch (TargetInvocationException failure) { throw new InvalidOperationException("shared CLI apply must retain recovery identity after committed I/O failure", failure.InnerException); }
                var type = result.GetType();
                Require((int)type.GetProperty("Exit")!.GetValue(result)! == 7 && (string)type.GetProperty("Outcome")!.GetValue(result)! == "RecoveryRequired", "unconfirmed journal state cannot be reported as success or wholly skipped");
                using var json = JsonDocument.Parse(CommandOutputJson.Write("convert", type.GetProperty("Data")!.GetValue(result))); var data = json.RootElement.GetProperty("data");
                Require(data.GetProperty("planHash").GetString() == prepared.Plan.PlanHash.Hex && !data.GetProperty("receiptIsTerminal").GetBoolean()
                    && data.GetProperty("recovery").GetProperty("world").GetString() == Path.GetFullPath(world)
                    && data.GetProperty("recovery").GetProperty("stateRoot").GetString() == Path.GetFullPath(state), "error keeps exact plan and actual world/state root for resumption");
                if (mode.StartsWith("terminal", StringComparison.Ordinal))
                    Require(data.GetProperty("unconfirmedOperations").GetProperty("requested").GetInt32() == 3
                        && data.GetProperty("unconfirmedOperations").GetProperty("results").EnumerateArray().All(item => item.GetProperty("outcome").GetString() == "recoveryRequired")
                        && data.GetProperty("receipt").ValueKind == JsonValueKind.Null, "without a durable receipt all known requests remain conservatively unconfirmed, never a forged history revision");
                else Require(ApplyReceiptJson.Read(data.GetProperty("receipt").GetRawText(), prepared.Plan, prepared.Manifest).Success, "a returned persisted receipt is retained when the subsequent status read fails");
                var first = plan.Preconditions.Single(input => input.Artifact.RelativePath == WorldQueryTests.Function(0));
                Require(Hash256.Compute(File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0)))) != first.Image.Hash, "witness reaches a real committed world image");
                journals.Disabled = true;
                var recovery = data.GetProperty("recovery");
                using var recovered = Success(Run(state, "recover", recovery.GetProperty("world").GetString()!, "--state-root", recovery.GetProperty("stateRoot").GetString()!, "--plan-hash", data.GetProperty("planHash").GetString()!, "--world-offline"));
                Require(recovered.RootElement.GetProperty("data").GetProperty("receiptIsTerminal").GetBoolean(), "the error response's exact identity resumes the same journal through a real CLI process");
            }
            finally { Directory.Delete(world, true); Directory.Delete(state, true); }
        }
    }
    private sealed class FaultJournal(IApplyJournalStore inner, string mode) : IApplyJournalStore
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public bool Disabled { get; set; }
        private bool terminal;
        public byte[]? Read(Hash256 hash) => !Disabled && terminal && mode == "status" ? throw new IOException("Injected postapply status I/O failure") : inner.Read(hash);
        public void Write(Hash256 hash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            var phase = ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span)).Phase;
            if (!Disabled && phase is ApplyJournalPhase.Completed or ApplyJournalPhase.RecoveryRequired)
            {
                if (mode == "terminal") throw new IOException("Injected terminal pointer I/O failure");
                if (mode == "terminal-missing") throw new FileNotFoundException("Injected missing terminal pointer dependency after commit");
                inner.Write(hash, bytes, expected); terminal = true; return;
            }
            inner.Write(hash, bytes, expected);
        }
    }
}
