using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Next.Verification;

internal sealed record CaseResult(string Name, string Suite, string Status, string? Error = null, int? WorkerPid = null);

internal static class VerificationRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = RunnerOptions.Parse(args);
            var selected = CaseRegistry.Cases.Where(item =>
                (options.Suite == "all" || item.Suite == options.Suite)
                && (options.Case is null ? item.Suite != "calibration" : item.Name == options.Case)).ToArray();
            if (selected.Length == 0) throw new ArgumentException("沒有符合的 suite/case；未執行不能視為通過");
            if (options.Worker)
            {
                if (options.Case is null || selected.Length != 1) throw new ArgumentException("Worker 必須指定單一 case");
                var result = RunCase(selected[0], options);
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return result.Status == "Passed" ? 0 : 1;
            }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                var results = new List<CaseResult>();
                foreach (var item in selected)
                    results.Add(cancellation.IsCancellationRequested
                        ? new(item.Name, item.Suite, "Cancelled")
                        : await RunWorkerAsync(item, options, cancellation.Token));
                var passed = results.Count(item => item.Status == "Passed");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaFamily = "nbtcli.next.verification", schemaVersion = 1, gateRevision = 2,
                    selected = results.Count, passed, failed = results.Count - passed, cases = results
                }, JsonOptions));
                return cancellation.IsCancellationRequested ? 130 : passed == results.Count ? 0 : 1;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            var code = error is ArgumentException or IOException or UnauthorizedAccessException
                ? "INVALID_VERIFICATION_REQUEST" : "VERIFICATION_ERROR";
            Console.WriteLine(JsonSerializer.Serialize(new { success = false, code, error = error.Message }, JsonOptions));
            return 2;
        }
    }

    private static CaseResult RunCase(VerificationCase item, RunnerOptions options)
    {
        try
        {
            item.Test(new(options.FixtureRoot, options.ArtifactRoot));
            return new(item.Name, item.Suite, "Passed");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{item.Name}: {error.Message}");
            return new(item.Name, item.Suite, error is NotSupportedException ? "Unsupported" : "Failed", error.Message);
        }
    }

    private static async Task<CaseResult> RunWorkerAsync(VerificationCase item, RunnerOptions options, CancellationToken cancellation)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "nbtcli-next-verification", "worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try { return await RunWorkerInScratchAsync(item, options, cancellation, scratch); }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static async Task<CaseResult> RunWorkerInScratchAsync(VerificationCase item, RunnerOptions options,
        CancellationToken cancellation, string scratch)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.Environment["NBTCLI_VERIFICATION_SCRATCH"] = scratch;
        foreach (var argument in new[] { typeof(VerificationRunner).Assembly.Location, "--worker", "--case", item.Name,
            "--fixture-root", options.FixtureRoot }) start.ArgumentList.Add(argument);
        if (options.ArtifactRoot is not null)
        {
            start.ArgumentList.Add("--artifact-root");
            start.ArgumentList.Add(options.ArtifactRoot);
        }
        using var process = Process.Start(start) ?? throw new IOException("無法啟動測試 worker");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(options.TimeoutMs);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
            if ((await stderr).Length > 0) Console.Error.Write(await stderr);
            var status = cancellation.IsCancellationRequested ? "Cancelled" : "TimedOut";
            Console.Error.WriteLine($"{item.Name}: {status}");
            return new(item.Name, item.Suite, status, $"Worker 已結束；deadline={options.TimeoutMs}ms", process.Id);
        }
        var errorText = await stderr;
        if (errorText.Length > 0) Console.Error.Write(errorText);
        try
        {
            var result = JsonSerializer.Deserialize<CaseResult>(await stdout, JsonOptions)
                ?? throw new JsonException("Worker 沒有回執");
            if (result.Name != item.Name || result.Suite != item.Suite || result.Status is not ("Passed" or "Failed" or "Unsupported")
                || (process.ExitCode == 0) != (result.Status == "Passed"))
                throw new JsonException("Worker 回執與選定案例或退出碼不符");
            return result with { WorkerPid = process.Id };
        }
        catch (JsonException error) { return new(item.Name, item.Suite, "Failed", error.Message, process.Id); }
    }
}
