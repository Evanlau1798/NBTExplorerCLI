using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.SessionExchangeTests;
using static NBTExplorerCLI.Verification.TranslationSessionTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionProcessTests
{
    private sealed class Child : IDisposable
    {
        internal Process Process { get; }
        private readonly Task<string> output, error;
        internal Child(string root, string mode, string role = "")
        {
            var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            foreach (var argument in new[] { typeof(SessionProcessTests).Assembly.Location, "--worker", "--case", "session-process-child" }) start.ArgumentList.Add(argument);
            start.Environment["NBTCLI_SESSION_TEST_ROOT"] = root; start.Environment["NBTCLI_SESSION_TEST_MODE"] = mode; start.Environment["NBTCLI_SESSION_TEST_ROLE"] = role;
            Process = System.Diagnostics.Process.Start(start)!; output = Process.StandardOutput.ReadToEndAsync(); error = Process.StandardError.ReadToEndAsync();
        }
        internal async Task Finish(bool killed = false)
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await Task.WhenAll(output, error);
            Require(killed || Process.ExitCode == 0, "session child failed: " + await error + await output);
        }
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); _ = Process.WaitForExit(10_000); }
            Process.Dispose();
        }
    }
    private static async Task Ready(string path, params Child[] children)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!File.Exists(path))
        {
            if (children.Any(child => child.Process.HasExited) || DateTime.UtcNow >= deadline) throw new TimeoutException("Session child did not reach its controlled persistence phase");
            await Task.Delay(20);
        }
    }
    public static void TwoProcessesCannotSaveOneRevisionTwice() => TwoProcesses().GetAwaiter().GetResult();
    private static async Task TwoProcesses()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(root, "sessions"))); service.Start(StartRequest(), Guid.NewGuid());
            var page = service.Export(SessionId, 0);
            foreach (var role in new[] { "a", "b" }) File.WriteAllText(Path.Combine(root, role + ".json"), SessionExchangeJson.WriteReply(Reply(page)), new UTF8Encoding(false));
            using var first = new Child(root, "cas", "a"); using var second = new Child(root, "cas", "b");
            await Ready(Path.Combine(root, "a.ready"), first, second); await Ready(Path.Combine(root, "b.ready"), first, second);
            File.WriteAllText(Path.Combine(root, "go"), "go"); await Task.WhenAll(first.Finish(), second.Finish());
            var outcomes = new[] { File.ReadAllText(Path.Combine(root, "a.result")), File.ReadAllText(Path.Combine(root, "b.result")) };
            Require(outcomes.Count(value => value == "applied") == 1 && outcomes.Count(value => value == "conflict") == 1 && service.Read(SessionId).Revision == 1,
                "two fresh processes read the same revision, but exactly one durable CAS and receipt can advance it");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void KilledSaveAndLostReceiptRemainRecoverable() => KillWindows().GetAwaiter().GetResult();
    private static async Task KillWindows()
    {
        foreach (var mode in new[] { "before", "after" })
        {
            var root = ScratchDirectory.CreateChild();
            try
            {
                var store = new FileSystemTranslationSessionStore(Path.Combine(root, "sessions")); var service = new TranslationSessionService(store); service.Start(StartRequest(), Guid.NewGuid());
                var old = store.Read(SessionId)!; var reply = Reply(service.Export(SessionId, 0)); var bytes = Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)); var input = Path.Combine(root, "reply.json"); File.WriteAllBytes(input, bytes);
                using var child = new Child(root, mode);
                await Ready(Path.Combine(root, mode + ".ready"), child); child.Process.Kill(entireProcessTree: true); await child.Finish(killed: true);
                var observed = service.Read(SessionId);
                Require(observed.Revision == (mode == "before" ? 0 : 1) && (mode != "before" || store.Read(SessionId)!.SequenceEqual(old)),
                    "killing before replacement retains the old complete head; killing after save retains the new complete head");
                var receipt = service.Submit(SessionId, File.ReadAllBytes(input));
                Require(receipt.OperationId == reply.OperationId && receipt.Revision == 1 && service.Read(SessionId).Revision == 1
                    && service.Read(SessionId).Operations.Length == 2 && File.ReadAllBytes(input).SequenceEqual(bytes), "retry after either real process interruption keeps the input and recovers exactly one saved operation");
            }
            finally { Directory.Delete(root, true); }
        }
    }
    private sealed class GatedStore(ITranslationSessionStore actual, string root, string role, bool after) : ITranslationSessionStore
    {
        private int read;
        public byte[]? Read(Guid id)
        {
            var bytes = actual.Read(id);
            if (!after && Interlocked.Exchange(ref read, 1) == 0)
            {
                File.WriteAllText(Path.Combine(root, role + ".ready"), "ready");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(Path.Combine(root, "go"))) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("CAS release not received"); Thread.Sleep(20); }
            }
            return bytes;
        }
        public void Write(Guid id, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            actual.Write(id, bytes, expected);
            if (after) { File.WriteAllText(Path.Combine(root, "after.ready"), "saved"); using var blocked = new ManualResetEventSlim(); blocked.Wait(); }
        }
    }
    private sealed class PausedFlush(string root) : ArtifactFileIo
    {
        public override void Flush(FileStream output)
        {
            base.Flush(output); File.WriteAllText(Path.Combine(root, "before.ready"), "flushed"); using var blocked = new ManualResetEventSlim(); blocked.Wait();
        }
    }
    public static void ProcessChild()
    {
        var root = Environment.GetEnvironmentVariable("NBTCLI_SESSION_TEST_ROOT") ?? throw new InvalidOperationException("Missing controlled session root");
        var scratch = Environment.GetEnvironmentVariable("NBTCLI_VERIFICATION_SCRATCH") ?? throw new InvalidOperationException("Missing controlled scratch");
        Require(Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratch)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "session process only accepts controller scratch");
        var mode = Environment.GetEnvironmentVariable("NBTCLI_SESSION_TEST_MODE"); var role = Environment.GetEnvironmentVariable("NBTCLI_SESSION_TEST_ROLE")!; var storeRoot = Path.Combine(root, "sessions");
        if (mode == "cas")
        {
            Require(role is "a" or "b", "unknown controlled CAS role");
            var service = new TranslationSessionService(new GatedStore(new FileSystemTranslationSessionStore(storeRoot), root, role, false));
            try { service.Submit(SessionId, File.ReadAllBytes(Path.Combine(root, role + ".json"))); File.WriteAllText(Path.Combine(root, role + ".result"), "applied"); }
            catch (IOException) { File.WriteAllText(Path.Combine(root, role + ".result"), "conflict"); }
        }
        else
        {
            Require(mode is "before" or "after", "unknown controlled persistence phase");
            ITranslationSessionStore store = mode == "before" ? new FileSystemTranslationSessionStore(storeRoot, new PausedFlush(root))
                : new GatedStore(new FileSystemTranslationSessionStore(storeRoot), root, role, true);
            new TranslationSessionService(store).Submit(SessionId, File.ReadAllBytes(Path.Combine(root, "reply.json")));
            throw new InvalidOperationException("Expected controller to interrupt persistence child");
        }
    }
}
