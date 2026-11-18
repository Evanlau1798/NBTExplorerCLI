using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldBackupProcessTests
{
    private sealed class PausedStore(IWorldBackupStore inner, string root, bool after) : IWorldBackupStore
    {
        public byte[]? ReadBlob(Hash256 hash) => inner.ReadBlob(hash);
        public byte[]? ReadManifest(Hash256 hash) => inner.ReadManifest(hash);
        public void SaveBlob(Hash256 hash, ReadOnlyMemory<byte> bytes) => inner.SaveBlob(hash, bytes);
        public void SaveManifest(Hash256 hash, ReadOnlyMemory<byte> bytes)
        {
            if (after) inner.SaveManifest(hash, bytes);
            File.WriteAllText(Path.Combine(root, "ready"), "ready"); using var pause = new ManualResetEventSlim(); pause.Wait();
            if (!after) inner.SaveManifest(hash, bytes);
        }
    }
    public static void CompletionSurvivesRealProcessInterruptions() => Run().GetAwaiter().GetResult();
    private static async Task Run()
    {
        foreach (var after in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild();
            try
            {
                var worldRoot = Path.Combine(root, "world"); var stateRoot = Path.Combine(root, "backup"); Directory.CreateDirectory(worldRoot);
                Put(worldRoot, "a.dat", [1, 2, 3]); var artifact = new ArtifactAddress(World, "a.dat", ArtifactKind.Nbt); var image = ArtifactImage.Present(Hash256.Compute(new byte[] { 1, 2, 3 }));
                var id = Hash256.Compute(Encoding.UTF8.GetBytes(BackupManifestJson.Write(new(World, [new(artifact, image)]))));
                var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
                var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
                foreach (var argument in new[] { typeof(WorldBackupProcessTests).Assembly.Location, "--worker", "--case", "backup-capture-child" }) start.ArgumentList.Add(argument);
                start.Environment["NBTCLI_BACKUP_TEST_ROOT"] = root; start.Environment["NBTCLI_BACKUP_TEST_AFTER"] = after ? "true" : "false";
                using var child = Process.Start(start)!; var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(8);
                    while (!File.Exists(Path.Combine(root, "ready")))
                    {
                        if (child.HasExited || DateTime.UtcNow >= deadline) throw new TimeoutException("Backup child did not reach the controlled completion boundary");
                        await Task.Delay(20);
                    }
                    child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await Task.WhenAll(output, error);
                    var store = new FileSystemWorldBackupStore(stateRoot); var service = new WorldBackupService(new WorldInventory(worldRoot, World), store);
                    if (after) Require(service.Load(id).ReadArtifact(artifact)!.SequenceEqual(new byte[] { 1, 2, 3 }), "fresh process observes the complete verified backup after publication");
                    else { Require(store.ReadManifest(id) is null, "killing before publication leaves no completed manifest"); Reject<IOException>(() => service.Load(id)); }
                    Require(service.Create([new(artifact, image)]).ManifestHash == id && File.ReadAllBytes(Path.Combine(worldRoot, "a.dat")).SequenceEqual(new byte[] { 1, 2, 3 }), "retry after either process interruption converges to the same immutable backup without source writes");
                }
                finally
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    await Task.WhenAll(output, error);
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
    public static void CaptureChild()
    {
        var root = Environment.GetEnvironmentVariable("NBTCLI_BACKUP_TEST_ROOT") ?? throw new InvalidOperationException("Missing controlled backup root");
        var scratch = Environment.GetEnvironmentVariable("NBTCLI_VERIFICATION_SCRATCH") ?? throw new InvalidOperationException("Missing controlled scratch");
        Require(Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "backup child only accepts controller scratch");
        var after = Environment.GetEnvironmentVariable("NBTCLI_BACKUP_TEST_AFTER") switch { "true" => true, "false" => false, _ => throw new InvalidOperationException("Missing interruption mode") };
        var store = new PausedStore(new FileSystemWorldBackupStore(Path.Combine(root, "backup")), root, after);
        _ = new WorldBackupService(new WorldInventory(Path.Combine(root, "world"), World), store).Create([new(new(World, "a.dat", ArtifactKind.Nbt), ArtifactImage.Present(Hash256.Compute(new byte[] { 1, 2, 3 })))]);
    }
}
