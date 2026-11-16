using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class WorldApplyProcessTests
{
    public static void RequiredWindowsSurviveProcessKills() => Run().GetAwaiter().GetResult();
    private static async Task Run()
    {
        foreach (var phase in new[] { "before-first", "after-first", "after-n", "before-receipt", "recover-again", "rollback-again", "delete-after-mark" })
        {
            var root = ScratchDirectory.CreateChild(); var world = WorldQueryTests.CreateWorld(); var state = Path.Combine(root, "state");
            try
            {
                var originals = Enumerable.Range(0, 3).ToDictionary(index => WorldQueryTests.Function(index), index => File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(index))));
                if (phase == "rollback-again")
                {
                    var storage = new ArtifactStorage(world, World); using var prepared = WorldApplyTests.Functions(world, state, storage);
                    Require(WorldApplyTests.Engine(world, state, storage).Apply(prepared, true).Success, "rollback kill control begins with a fully applied plan");
                    File.WriteAllText(Path.Combine(root, "plan-hash"), prepared.Plan.PlanHash.Hex);
                    await KillAt(root, world, state, "after-first", recover: true, rollback: true);
                    var restarted = WorldApplyTests.Engine(world, state); var restored = restarted.Recover(prepared.Plan.PlanHash, true);
                    Require(!restored.Success && restored.Counts[OperationOutcome.RolledBack] == 3 && originals.All(pair => File.ReadAllBytes(Path.Combine(world, pair.Key)).SequenceEqual(pair.Value)),
                        "persisted rollback direction survives a kill after its first restore and resumes to all verified original preimages");
                    Require(ApplyReceiptJson.Write(restarted.Recover(prepared.Plan.PlanHash, true)) == ApplyReceiptJson.Write(restored), "repeated rollback recovery preserves its receipt revision");
                    continue;
                }
                await KillAt(root, world, state, phase == "recover-again" ? "after-first" : phase, recover: false);
                var planHash = new Hash256(File.ReadAllText(Path.Combine(root, "plan-hash")));
                if (phase == "delete-after-mark")
                {
                    Require(!File.Exists(Path.Combine(world, "remove.json")), "killing after handle disposition closes the marked source and publishes absence");
                    var restored = WorldApplyTests.Engine(world, state).Recover(planHash, true);
                    Require(restored.Success && restored.Requested == 1 && restored.Counts[OperationOutcome.Applied] == 1 && !File.Exists(Path.Combine(world, "remove.json"))
                        && originals.All(pair => File.ReadAllBytes(Path.Combine(world, pair.Key)).SequenceEqual(pair.Value)), "fresh recovery recognizes the absent postimage without recreating an empty file or changing unrelated functions");
                    continue;
                }
                if (phase == "before-first") Require(originals.All(pair => File.ReadAllBytes(Path.Combine(world, pair.Key)).SequenceEqual(pair.Value)), "killing before first replacement leaves every original function unchanged");
                var journal = ApplyJournalJson.Read(Encoding.UTF8.GetString(new FileSystemApplyJournalStore(Path.Combine(state, "journals")).Read(planHash)!));
                if (phase == "before-receipt") Require(journal.Phase == ApplyJournalPhase.ReadBack && journal.ReceiptHash is null, "all readbacks precede receipt publication in the controlled kill window");
                if (phase == "recover-again")
                {
                    File.Delete(Path.Combine(root, "ready"));
                    await KillAt(root, world, state, "after-first", recover: true);
                }
                var engine = WorldApplyTests.Engine(world, state); var receipt = engine.Recover(planHash, true);
                Require(receipt.Success && receipt.Requested == 3 && receipt.Counts[OperationOutcome.Applied] == 3, "fresh recovery after each real kill classifies and completes the same three operations");
                for (var index = 0; index < 3; index++) Require(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(index)))) == "tellraw @a[name=Guard] \"譯:Text" + index + "\"\n", "recovery preserves framing and publishes each exact postimage once");
                Require(ApplyReceiptJson.Write(engine.Recover(planHash, true)) == ApplyReceiptJson.Write(receipt), "repeated recovery returns the current persisted receipt version");
            }
            finally { Directory.Delete(world, true); Directory.Delete(root, true); }
        }
    }
    private static async Task KillAt(string root, string world, string state, string phase, bool recover, bool rollback = false)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var argument in new[] { typeof(WorldApplyProcessTests).Assembly.Location, "--worker", "--case", "apply-process-child" }) start.ArgumentList.Add(argument);
        start.Environment["NBTCLI_APPLY_TEST_ROOT"] = root; start.Environment["NBTCLI_APPLY_TEST_WORLD"] = world; start.Environment["NBTCLI_APPLY_TEST_STATE"] = state;
        start.Environment["NBTCLI_APPLY_TEST_PHASE"] = phase; start.Environment["NBTCLI_APPLY_TEST_RECOVER"] = recover ? "true" : "false";
        start.Environment["NBTCLI_APPLY_TEST_ROLLBACK"] = rollback ? "true" : "false";
        using var child = Process.Start(start)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (child.HasExited || DateTime.UtcNow >= deadline) throw new TimeoutException("Apply child did not reach " + phase + "; " + (child.HasExited ? await error : "deadline"));
                await Task.Delay(20);
            }
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            await Task.WhenAll(output, error);
        }
    }
    private static void Pause(string root)
    { File.WriteAllText(Path.Combine(root, "ready"), "ready"); using var stop = new ManualResetEventSlim(); stop.Wait(); }
    private sealed class PausedIo(string root, string phase) : ArtifactFileIo
    {
        private int count;
        public override void Replace(string candidate, string target, bool absent)
        {
            var selected = ++count == (phase == "after-n" ? 2 : 1);
            if (selected && phase == "before-first") Pause(root);
            base.Replace(candidate, target, absent);
            if (selected && phase is "after-first" or "after-n") Pause(root);
        }
        public override void Delete(FileStream source)
        { base.Delete(source); if (phase == "delete-after-mark") Pause(root); }
    }
    private sealed class PausedReceipt(IApplyJournalStore inner, string root) : IApplyJournalStore
    {
        public byte[]? Read(Hash256 planHash) => inner.Read(planHash);
        public void Write(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            if (ApplyJournalJson.Read(Encoding.UTF8.GetString(bytes.Span)).Phase == ApplyJournalPhase.Completed) Pause(root);
            inner.Write(planHash, bytes, expected);
        }
    }
    public static void Child()
    {
        var scratch = Environment.GetEnvironmentVariable("NBTCLI_VERIFICATION_SCRATCH") ?? throw new InvalidOperationException("Missing controller scratch");
        string Controlled(string name)
        {
            var path = Path.GetFullPath(Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing controlled path"));
            Require(path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratch)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "apply child accepts only controller scratch paths"); return path;
        }
        var root = Controlled("NBTCLI_APPLY_TEST_ROOT"); var world = Controlled("NBTCLI_APPLY_TEST_WORLD"); var state = Controlled("NBTCLI_APPLY_TEST_STATE");
        var phase = Environment.GetEnvironmentVariable("NBTCLI_APPLY_TEST_PHASE")!;
        Require(phase is "before-first" or "after-first" or "after-n" or "before-receipt" or "delete-after-mark", "unknown controlled apply phase");
        var storage = new ArtifactStorage(world, World, new PausedIo(root, phase));
        IApplyJournalStore journal = new FileSystemApplyJournalStore(Path.Combine(state, "journals")); if (phase == "before-receipt") journal = new PausedReceipt(journal, root);
        var engine = new WorldApplyEngine(new WorldInventory(world, World), storage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")), journal);
        if (Environment.GetEnvironmentVariable("NBTCLI_APPLY_TEST_RECOVER") == "true") engine.Recover(new(File.ReadAllText(Path.Combine(root, "plan-hash"))), true,
            rollback: Environment.GetEnvironmentVariable("NBTCLI_APPLY_TEST_ROLLBACK") == "true");
        else
        {
            using var prepared = phase == "delete-after-mark" ? Delete(world, root, storage) : WorldApplyTests.Functions(world, state, storage);
            File.WriteAllText(Path.Combine(root, "plan-hash"), prepared.Plan.PlanHash.Hex);
            engine.Apply(prepared, true);
        }
        throw new InvalidOperationException("Expected controller to kill the apply child");
    }
    private static PreparedWorldPlan Delete(string world, string root, IArtifactStorage storage)
    {
        var artifact = new ArtifactAddress(World, "remove.json", ArtifactKind.Json); var inventory = new WorldInventory(world, World);
        var backup = new WorldBackupService(inventory, new FileSystemWorldBackupStore(Path.Combine(root, "restore-source"))).Create([new(artifact, ArtifactImage.Absent)]);
        File.WriteAllBytes(Path.Combine(world, artifact.RelativePath), [7]);
        var operation = new RestorePatch("explicit-delete", new(backup.ManifestHash, backup.Manifest.Artifacts,
            new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = ArtifactImage.Present(Hash256.Compute(new byte[] { 7 })) }));
        var plan = new WorldPlanBuilder(inventory).Build(backup, [operation]);
        return new WorldPlanPreparation(inventory, storage).Prepare(backup, plan, true);
    }
}
