using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Next.Benchmarks;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class BenchmarkCommandTests
{
    private static (int Exit, JsonDocument Json) Run(params string[] arguments)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(CorpusBuilder).Assembly.Location); foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Benchmark command did not finish"); }
        Require(error.GetAwaiter().GetResult().Length == 0, "benchmark errors remain in the single structured response");
        return (process.ExitCode, JsonDocument.Parse(output.GetAwaiter().GetResult()));
    }

    public static void PublicMeasurementUsesRealSourcesAndKeepsIncompleteResults()
    {
        var world = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var full = Run("measure-scan", world, cache, "full", "1"); using var f = full.Json;
            var warm = Run("measure-scan", world, cache, "reuse", "1"); using var w = warm.Json;
            Require(full.Exit == 0 && warm.Exit == 0 && f.RootElement.GetProperty("schemaFamily").GetString() == "nbtcli.next.benchmark"
                && f.RootElement.GetProperty("data").GetProperty("counts").GetProperty("occurrences").GetInt32() == 4
                && f.RootElement.GetProperty("data").GetProperty("semanticHash").GetString() == w.RootElement.GetProperty("data").GetProperty("semanticHash").GetString()
                && w.RootElement.GetProperty("data").GetProperty("query").GetProperty("scannedArtifacts").GetInt32() == 0, "real process emits full metrics and verified reuse of identical semantics");
            WorldDiscoveryTests.Put(world, "resourcepacks/r/pack.mcmeta", System.Text.Encoding.UTF8.GetBytes(WorldDiscoveryTests.ResourcePack));
            WorldDiscoveryTests.Put(world, "resourcepacks/r/assets/test/lang/en_us.json", "{\"fixture\":\"Language\"}"u8.ToArray());
            var scoped = Run("measure-scan", world, cache, "full", "1", "language.value"); using var s = scoped.Json;
            Require(scoped.Exit == 0 && s.RootElement.GetProperty("data").GetProperty("counts").GetProperty("occurrences").GetInt64() == 1
                && s.RootElement.GetProperty("configuration").GetProperty("scope").GetString() == "language.value", "explicit language scope retains its real slot while excluding command and metadata text");
            WorldDiscoveryTests.Put(world, "data/bad.dat", [1, 2, 3]);
            var broken = Run("measure-scan", world, cache, "full", "1"); using var b = broken.Json;
            Require(broken.Exit == 4 && !b.RootElement.GetProperty("success").GetBoolean()
                && b.RootElement.GetProperty("data").GetProperty("counts").GetProperty("failed").GetInt32() == 1
                && !b.RootElement.GetProperty("data").GetProperty("coverageComplete").GetBoolean(), "a measured incomplete workload remains nonzero with its actual diagnostics/counts available");
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    public static void InvalidCommandsCannotCreateStateOrWriteWorlds()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            foreach (var pair in new[] { ("full", "9"), ("unknown", "1"), ("full", "x") })
            {
                var state = Path.Combine(work, Guid.NewGuid().ToString("N"));
                var result = Run("measure-scan", world, state, pair.Item1, pair.Item2); using var json = result.Json;
                Require(result.Exit == 2 && !Directory.Exists(state), "invalid mode/limits refuse before any cache publication");
            }
            var inside = Path.Combine(world, "forbidden-cache"); var refused = Run("measure-scan", world, inside, "full", "1"); using var output = refused.Json;
            Require(refused.Exit == 2 && !Directory.Exists(inside), "benchmark cache cannot mutate the measured world tree");
            var badScope = Path.Combine(work, "bad-scope"); var invalid = Run("measure-scan", world, badScope, "full", "1", "unknown"); using var scopeError = invalid.Json;
            Require(invalid.Exit == 2 && !Directory.Exists(badScope), "unknown scope refuses before cache creation");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }

    public static void ExistingCorpusGenerationRetainsItsFrozenOutput()
    {
        var work = ScratchDirectory.CreateChild();
        try
        {
            var root = Path.Combine(work, "corpus"); var result = Run("generate-corpus", "S", root); using var output = result.Json;
            var data = output.RootElement.GetProperty("data");
            Require(result.Exit == 0 && data.GetProperty("Files").GetInt32() == 6 && data.GetProperty("Slots").GetInt32() == 256
                && data.GetProperty("ManifestSha256").GetString() == "33277bc56c3504cc181df5b24132eb207e5f4f06d65a8efdc8c684a73bad3019",
                "the new asynchronous measurement entry preserves the existing corpus command and its exact recipe hash");
        }
        finally { Directory.Delete(work, true); }
    }

    public static void CancelledCommandReturnsStructuredFailureBeforeCacheCreation()
    {
        var world = WorldQueryTests.CreateWorld();
        var work = ScratchDirectory.CreateChild();
        try
        {
            var cache = Path.Combine(work, "cache");
            using var output = new StringWriter();
            var exit = BenchmarkScanCommand.RunAsync(["measure-scan", world, cache, "full", "1"],
                output, new CancellationToken(true)).GetAwaiter().GetResult();
            using var result = JsonDocument.Parse(output.ToString());
            Require(exit == 130 && !result.RootElement.GetProperty("success").GetBoolean()
                && result.RootElement.GetProperty("error").GetProperty("code").GetString() == "CANCELLED"
                && !Directory.Exists(cache), "pre-cancelled measurement must return structured failure without creating a cache");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
