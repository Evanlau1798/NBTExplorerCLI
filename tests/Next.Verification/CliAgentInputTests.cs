using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Next.Cli;
using static Next.Verification.CliScanTests;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CliAgentInputTests
{
    public static void OversizedStdinIsRefusedBeforeEof()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var state = Path.Combine(work, "state"); using var setup = Success(Run(work, "agent", "setup", world, "--state-root", state));
            var answer = JsonNode.Parse(setup.RootElement.GetProperty("data").GetProperty("questionnaire").GetRawText())!;
            answer["data"]!["configuration"]!["agentName"] = "stdin-agent"; answer["data"]!["configuration"]!["sourceLanguage"] = "en_us";
            using var accepted = Success(RunInput(work, answer.ToJsonString(), "agent", "submit", world, "--state-root", state, "--request", "-"));
            var stored = Path.Combine(state, "agent", "configuration.json"); var before = File.ReadAllBytes(stored);
            var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
            var start = new ProcessStartInfo(host) { WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { typeof(CliApplication).Assembly.Location, "agent", "submit", world, "--state-root", state, "--request", "-" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start CLI input control");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { process.StandardInput.Write(new string('x', 16_385)); process.StandardInput.Flush(); } catch (IOException) { }
            var beforeEof = process.WaitForExit(5000); try { process.StandardInput.Close(); } catch (IOException) { }
            if (!process.WaitForExit(15_000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Input control did not finish after EOF; not a budget witness"); }
            using var result = JsonDocument.Parse(stdout.GetAwaiter().GetResult()); _ = stderr.GetAwaiter().GetResult();
            Require(process.ExitCode == 6 && result.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT"
                && File.ReadAllBytes(stored).SequenceEqual(before), "after-EOF control confirms resource classification and unchanged configuration");
            Require(beforeEof, "agent must stop reading after its 16384-byte budget, without waiting for the producer to close stdin; EOF control returned ResourceLimit");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
