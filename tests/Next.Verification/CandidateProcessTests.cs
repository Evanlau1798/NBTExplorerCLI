using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CandidateProcessTests
{
    public static void ReopenInFreshProcess()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); var before = CompressedNbt.WriteDocument(new("root", new NbtInt(42)), NbtCompression.GZip);
            var after = CompressedNbt.WriteDocument(new("root", new NbtLong(long.MaxValue)), NbtCompression.GZip);
            File.WriteAllBytes(path, before); var store = new ArtifactStorage(root, World); var artifact = new ArtifactAddress(World, "level.dat", ArtifactKind.Nbt);
            using var candidate = store.Stage(artifact, ArtifactImage.Present(Hash256.Compute(before)), after);
            candidate.Flush(); candidate.Verify(stream => _ = CompressedNbt.ReadDocument(stream, NbtCompression.GZip));
            Child(root, before); candidate.Commit(); Child(root, after);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void Child(string root, byte[] expected)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var argument in new[] { typeof(CandidateProcessTests).Assembly.Location, "--worker", "--case", "candidate-readback-child" }) start.ArgumentList.Add(argument);
        start.Environment["NBTCLI_CANDIDATE_READBACK_ROOT"] = root;
        start.Environment["NBTCLI_CANDIDATE_READBACK_HASH"] = Hash256.Compute(expected).Hex;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Readback child did not terminate"); }
        using var json = JsonDocument.Parse(output.GetAwaiter().GetResult());
        Require(process.ExitCode == 0 && json.RootElement.GetProperty("status").GetString() == "Passed", "fresh process readback failed: " + error.GetAwaiter().GetResult());
    }
    public static void ReadbackChild()
    {
        var root = Environment.GetEnvironmentVariable("NBTCLI_CANDIDATE_READBACK_ROOT") ?? throw new InvalidOperationException("Missing controlled readback root");
        var scratch = Environment.GetEnvironmentVariable("NBTCLI_VERIFICATION_SCRATCH")!;
        Require(Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "readback only accepts controller scratch");
        var bytes = new ArtifactStorage(root, World).Read(new(World, "level.dat", ArtifactKind.Nbt));
        Require(Hash256.Compute(bytes).Hex == Environment.GetEnvironmentVariable("NBTCLI_CANDIDATE_READBACK_HASH"), "disk image differs in fresh process");
        _ = CompressedNbt.ReadDocument(bytes, NbtCompression.GZip);
        CandidateIoTests.Reject<IOException>(() =>
        {
            using var competing = new ArtifactStorage(root, World).Stage(new(World, "level.dat", ArtifactKind.Nbt), ArtifactImage.Present(Hash256.Compute(bytes)), new byte[] { 0 });
        });
    }
}
