using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Cli;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class CliScanTests
{
    internal sealed record Invocation(int Exit, string Output);
    internal static Invocation Run(string cwd, params string[] args) => Invoke(cwd, args, null);
    internal static Invocation RunInput(string cwd, string input, params string[] args) => Invoke(cwd, args, input);
    private static Invocation Invoke(string cwd, string[] args, string? input)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.RedirectStandardInput = input is not null;
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location); foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start product CLI");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
        if (!process.WaitForExit(20_000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Product CLI did not finish"); }
        var output = stdout.GetAwaiter().GetResult(); var error = stderr.GetAwaiter().GetResult();
        Require(error.Length == 0 || process.ExitCode != 0, "successful machine output needs no diagnostic errors"); return new(process.ExitCode, output);
    }
    internal static JsonDocument Success(Invocation result)
    {
        Require(result.Exit == 0, "expected successful CLI: " + result.Output); var document = JsonDocument.Parse(result.Output);
        Require(document.RootElement.GetProperty("success").GetBoolean(), "success envelope agrees with exit code"); return document;
    }
    public static void FullWarmAndIncrementalUseTheSharedWorldCache()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            using var full = Success(Run(root, "scan", root, "--cache-root", cache, "--show-items", "--limit", "1", "--offset", "1"));
            var data = full.RootElement.GetProperty("data"); var id = data.GetProperty("snapshotId").GetRawText();
            Require(full.RootElement.GetProperty("command").GetString() == "scan" && data.GetProperty("coverageComplete").GetBoolean()
                && data.GetProperty("items").GetArrayLength() == 1 && data.GetProperty("pagination").GetProperty("total").GetInt32() >= 3
                && data.GetProperty("telemetry").GetProperty("scannedArtifacts").GetInt32() > 0, "real full scan has complete decoded text and pagination");
            using var warm = Success(Run(root, "extract", root, "--cache-root", cache)); var warmData = warm.RootElement.GetProperty("data");
            Require(warmData.GetProperty("snapshotId").GetRawText() == id && warmData.GetProperty("telemetry").GetProperty("manifestHits").GetInt32() == 1
                && warmData.GetProperty("telemetry").GetProperty("scannedArtifacts").GetInt32() == 0, "extract consumes the same verified cache as scan");
            var path = Path.Combine(root, WorldQueryTests.Function(0)); var time = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, File.ReadAllText(path).Replace("Text0", "Other", StringComparison.Ordinal), new UTF8Encoding(false)); File.SetLastWriteTimeUtc(path, time);
            using var incremental = Success(Run(root, "scan", root, "--cache-root", cache)); var changed = incremental.RootElement.GetProperty("data");
            Require(changed.GetProperty("snapshotId").GetRawText() != id && changed.GetProperty("telemetry").GetProperty("scannedArtifacts").GetInt32() == 1
                && changed.GetProperty("telemetry").GetProperty("reusedArtifacts").GetInt32() > 0, "CLI detects same-size same-mtime edits incrementally");
            using var refreshed = Success(Run(root, "scan", root, "--cache-root", cache, "--refresh"));
            Require(refreshed.RootElement.GetProperty("data").GetProperty("telemetry").GetProperty("reusedArtifacts").GetInt32() == 0, "refresh forces a real full scan");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void UniqueExtractionRetainsExactOccurrencesAndOutputLifecycle()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            for (var index = 0; index < 3; index++) Put(root, WorldQueryTests.Function(index), Encoding.UTF8.GetBytes("tellraw @a \"Same\"\n"));
            var output = Path.Combine(work, "extracted.json");
            using var extracted = Success(Run(work, "extract", root, "--no-cache", "--types", "McFunction", "--unique", "--all", "--output", output));
            var data = extracted.RootElement.GetProperty("data"); var texts = data.GetProperty("texts");
            Require(texts.GetArrayLength() == 1 && texts[0].GetProperty("instances").GetArrayLength() == 3
                && texts[0].GetProperty("instances").EnumerateArray().Select(item => item.GetProperty("address").GetRawText()).Distinct().Count() == 3,
                "dedup groups decoded text while retaining every exact source occurrence");
            Require(File.ReadAllText(output) == extracted.RootElement.GetRawText() + Environment.NewLine, "explicit output stores the same complete machine envelope");
            var before = File.ReadAllBytes(output); var repeated = Run(work, "extract", root, "--no-cache", "--output", output);
            Require(repeated.Exit == 3 && File.ReadAllBytes(output).SequenceEqual(before), "existing explicit output is preserved on conflict");
            using var filtered = Success(Run(work, "extract", root, "--no-cache", "--types", "command.function", "--match-pattern", "^missing$"));
            Require(filtered.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("total").GetInt32() == 0, "a real zero-result regex is successful");
            Require(Directory.GetFiles(work).Length == 1, "output leaves no temporary sibling files");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void IncompleteSourcesReturnPartialResultsWithNonzeroExit()
    {
        var root = WorldQueryTests.CreateWorld();
        try
        {
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("future_command @a\n"));
            var result = Run(root, "scan", root, "--no-cache", "--show-items", "--limit", "100"); using var document = JsonDocument.Parse(result.Output);
            var data = document.RootElement.GetProperty("data");
            Require(result.Exit == 4 && !document.RootElement.GetProperty("success").GetBoolean() && !data.GetProperty("coverageComplete").GetBoolean()
                && document.RootElement.GetProperty("error").GetProperty("code").GetString() == "UNSUPPORTED_SOURCE"
                && data.GetProperty("sources").EnumerateArray().Any(source => source.GetProperty("outcome").GetString() == "unsupported")
                && data.GetProperty("items").GetArrayLength() >= 2 && data.GetProperty("referenceCoverage").GetString() == "not-assessed",
                "unknown grammar retains readable siblings and cannot report complete world references or success");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ScopedNoCacheQueriesPreserveTheWorldAndValidateArguments()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            using var scoped = Success(Run(work, "scan", root, "--path", WorldQueryTests.Function(1), "--types", "McFunction", "--no-cache", "--show-items"));
            var data = scoped.RootElement.GetProperty("data"); Require(data.GetProperty("items").GetArrayLength() == 1
                && data.GetProperty("items")[0].GetProperty("text").GetString() == "Text1", "exact source scope reaches the decoder");
            foreach (var args in new[] { new[] { "--types", "Typo" }, new[] { "--workers", "9" }, new[] { "--summary", "--show-items" },
                new[] { "--match-pattern", "(" }, new[] { "--path", "../outside.dat" }, new[] { "--region", "bad" } })
            {
                var command = args[0] == "--match-pattern" ? "extract" : "scan";
                Require(Run(work, [command, root, "--no-cache", .. args]).Exit == 2, "invalid filters and mutually exclusive options must fail before writing state");
            }
            Require(before.Count == Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))
                && Directory.GetFiles(work).Length == 0, "query commands preserve the entire world and no-cache creates no caller-directory state");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void ValidationAndCapabilitiesExposeTheirActualScope()
    {
        var root = WorldQueryTests.CreateWorld();
        try
        {
            using var valid = Success(Run(root, "validate", root, "--no-cache"));
            Require(valid.RootElement.GetProperty("data").GetProperty("validationScope").GetString() == "decoded-text-sources", "validation declares its observable decoded source scope");
            using var quick = Success(Run(root, "validate", root, "--quick"));
            Require(quick.RootElement.GetProperty("data").GetProperty("validationScope").GetString() == "catalog", "quick mode cannot claim it decoded every payload");
            Put(root, "data/broken.dat", [0]); var broken = Run(root, "validate", root, "--no-cache", "--verbose"); using var failure = JsonDocument.Parse(broken.Output);
            Require(broken.Exit != 0 && !failure.RootElement.GetProperty("success").GetBoolean(), "corruption cannot be an apparently successful validation with issues");
            using var capabilities = Success(Run(root, "capabilities")); var profiles = capabilities.RootElement.GetProperty("data").GetProperty("profiles");
            Require(profiles.GetArrayLength() == 4 && profiles.EnumerateArray().All(profile => profile.GetProperty("features").EnumerateArray()
                .All(feature => feature.GetProperty("operations").GetProperty("write").GetString() != "supported")), "raw profile declarations do not infer write support from readability");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void CacheAndExportBoundariesRejectWorldWritesAndRegexTimeouts()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var guard = typeof(CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.ScanCommands")!.GetMethod("OutsideWorld", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var driveRejected = false;
            try { guard.Invoke(null, [Path.GetPathRoot(root)!, root]); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException?.Message == "Tool state and exports must be outside the input world") { driveRejected = true; }
            Require(driveRejected, "a drive root world includes all descendants; inspect the pure guard without scanning a user drive");
            var cache = Path.Combine(root, "cache"); var output = Path.Combine(root, "export.json");
            Require(Run(work, "scan", root, "--cache-root", cache).Exit == 2 && !Directory.Exists(cache), "a cache repository cannot be created inside the input world");
            Require(Run(work, "extract", root, "--no-cache", "--output", output).Exit == 2 && !File.Exists(output), "a query export cannot bypass the world writer boundary");
            var unowned = Path.Combine(work, "unowned"); Directory.CreateDirectory(unowned); var protectedFile = Path.Combine(unowned, "personal.txt"); File.WriteAllText(protectedFile, "keep");
            Require(Run(work, "scan", root, "--cache-root", unowned).Exit != 0 && File.ReadAllText(protectedFile) == "keep" && Directory.GetFiles(unowned).Length == 1,
                "cache ownership failure preserves all unrelated user files");
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("tellraw @a \"" + new string('a', 20_000) + "!\"\n"));
            var timed = Run(work, "extract", root, "--no-cache", "--types", "McFunction", "--match-pattern", "(a+)+$", "--output", Path.Combine(work, "timeout.json"));
            using var failure = JsonDocument.Parse(timed.Output);
            Require(timed.Exit == 6 && failure.RootElement.GetProperty("error").GetProperty("code").GetString() == "PATTERN_TIMEOUT"
                && !File.Exists(Path.Combine(work, "timeout.json")), "regex evaluation has an actual timeout and never publishes a partial output");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void LockedSourcesRemainReadErrorsAndUnknownRolesAreRejected()
    {
        var root = WorldQueryTests.CreateWorld();
        try
        {
            var readClassified = false; string? code;
            using (var locked = File.Open(Path.Combine(root, WorldQueryTests.Function(0)), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var result = Run(root, "scan", root, "--no-cache"); using var error = JsonDocument.Parse(result.Output);
                code = error.RootElement.GetProperty("error").GetProperty("code").GetString(); readClassified = result.Exit == 5 && code == "READ_ERROR";
            }
            var unknown = Run(root, "scan", root, "--no-cache", "--types", "command.function.typo");
            Require(readClassified && unknown.Exit == 2, $"locked sources retain ReadError and unknown role suffixes fail; locked={code}, unknownRoleExit={unknown.Exit}");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void DimensionRegionZipAndCorruptCacheKeepTheirExactBindings()
    {
        var root = WorldReferenceTests.CreateWorld(true); var cache = ScratchDirectory.CreateChild();
        try
        {
            using var selected = Success(Run(root, "scan", root, "--no-cache", "--dimension", "minecraft:overworld", "--region", "0,0", "--types", "ArmorStand", "--show-items"));
            var names = selected.RootElement.GetProperty("data").GetProperty("items");
            Require(names.GetArrayLength() == 2 && names.EnumerateArray().All(item => item.GetProperty("address").GetProperty("dimension").GetString() == "minecraft:overworld"
                && item.GetProperty("address").GetProperty("artifact").GetProperty("relativePath").GetString() == WorldReferenceTests.Entities), "dimension, coordinate and type filters retain both exact chunk bindings");
            foreach (var pack in new[] { "one", "two" }) Put(root, "datapacks/" + pack + ".zip", Zip(("pack.mcmeta", Format48Pack), ("data/test/function/shared.mcfunction", "tellraw @a \"Archived\"\n")));
            using var zipped = Success(Run(root, "extract", root, "--no-cache", "--unique", "--types", "McFunction", "--match-pattern", "^Archived$"));
            var instances = zipped.RootElement.GetProperty("data").GetProperty("texts")[0].GetProperty("instances");
            Require(instances.GetArrayLength() == 2 && instances.EnumerateArray().Select(item => item.GetProperty("address").GetProperty("artifact").GetProperty("relativePath").GetString()).Distinct().Count() == 2
                && instances.EnumerateArray().All(item => item.GetProperty("address").GetProperty("artifact").GetProperty("containerEntry").GetString() == "data/test/function/shared.mcfunction"), "dedup retains distinct ZIP artifacts sharing one entry name");
            using var full = Success(Run(root, "scan", root, "--cache-root", cache)); var id = full.RootElement.GetProperty("data").GetProperty("snapshotId").GetRawText();
            File.WriteAllText(Path.Combine(cache, "manifest.json"), "{");
            using var repaired = Success(Run(root, "scan", root, "--cache-root", cache)); var data = repaired.RootElement.GetProperty("data");
            Require(data.GetProperty("snapshotId").GetRawText() == id && data.GetProperty("telemetry").GetProperty("rebuilds").GetInt32() == 1
                && data.GetProperty("telemetry").GetProperty("scannedArtifacts").GetInt32() > 0, "corrupt persisted cache is rebuilt through the same real scan service");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void TransientOwnershipFailureNeverDeletesAnExistingDirectory()
    {
        var root = ScratchDirectory.CreateChild(); var marker = Path.Combine(root, "personal.txt"); File.WriteAllText(marker, "keep");
        try
        {
            try { using var directory = new TemporaryQueryDirectory(root); _ = new FileSystemScanCacheStore(directory.Path); }
            catch (IOException) { }
            Require(File.Exists(marker) && File.ReadAllText(marker) == "keep", "a rejected existing unowned transient directory must survive cleanup");
            var fresh = Path.Combine(root, "fresh");
            using (var directory = new TemporaryQueryDirectory(fresh)) { _ = new FileSystemScanCacheStore(directory.Path); Require(Directory.Exists(fresh), "fresh owned store exists during query"); }
            Require(!Directory.Exists(fresh), "the invocation's exclusively created directory is cleaned up");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static void QuickValidationSeparatesReadMalformedUnsupportedAndResourceFailures()
    {
        var root = WorldQueryTests.CreateWorld(); var failures = new List<string>();
        try
        {
            void Check(string expected, int exit)
            {
                var result = Run(root, "validate", root, "--quick"); using var json = JsonDocument.Parse(result.Output);
                var code = json.RootElement.GetProperty("error").GetProperty("code").GetString(); var outcome = json.RootElement.GetProperty("outcome").GetString();
                if (result.Exit != exit || code != expected || outcome != (exit == 4 ? "Unsupported" : "Failed")) failures.Add($"expected={expected}/{exit}, actual={code}/{result.Exit}/{outcome}");
            }
            var region = Path.Combine(root, "region", "r.0.0.mca"); Put(root, "region/r.0.0.mca", [0]); Check("MALFORMED_SOURCE", 5); File.Delete(region);
            using (var locked = File.Open(Path.Combine(root, "level.dat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check("READ_ERROR", 5);
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(9999, WorldMetadataTests.Dimensions), NbtCompression.GZip)); Check("UNSUPPORTED_SOURCE", 4);
            Level(root, false); var huge = Path.Combine(root, "data", "huge.dat"); Directory.CreateDirectory(Path.GetDirectoryName(huge)!);
            using (var stream = File.Create(huge)) stream.SetLength(67_108_865); Check("RESOURCE_LIMIT", 6);
            Require(failures.Count == 0, "quick catalog classification: " + string.Join(" | ", failures));
        }
        finally { Directory.Delete(root, true); }
    }
}
