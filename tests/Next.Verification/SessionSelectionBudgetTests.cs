using System.Text;
using System.Text.Json;
using Next.Cli;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class SessionSelectionBudgetTests
{
    public static void SelectionPrecedesTheSessionSlotLimit()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            for (var index = 0; index < 3; index++) File.Delete(Path.Combine(world, WorldQueryTests.Function(index)));
            var lines = string.Concat(Enumerable.Repeat("tellraw @a \"skip\"\n", 100));
            for (var index = 0; index < 1000; index++) Put(world, $"datapacks/p/data/test/function/s{index:D6}.mcfunction", Encoding.UTF8.GetBytes(lines));
            Put(world, "datapacks/p/data/test/function/z-last.mcfunction", Encoding.UTF8.GetBytes("tellraw @a \"selected-A\"\ntellraw @a \"selected-B\"\n"));
            (int Exit, JsonDocument Json) Start(params string[] extra)
            {
                using var output = new StringWriter();
                var exit = CliApplication.Run(["translate", "start", world, "--state-root", Path.Combine(work, "state"), "--session-id", Guid.NewGuid().ToString(),
                    "--source-language", "en_us", "--target-language", "zh_tw", "--types", "McFunction", "--no-group", .. extra], output);
                return (exit, JsonDocument.Parse(output.ToString()));
            }
            // Real query/NTFS with 100002 occurrences; invoke the CLI entry point inside this deadline-bounded worker.
            var selected = Start("--match-pattern", "^selected-"); using var selectedJson = selected.Json;
            var count = selectedJson.RootElement.GetProperty("data").ValueKind == JsonValueKind.Object
                && selectedJson.RootElement.GetProperty("data").TryGetProperty("status", out var status) ? status.GetProperty("total").GetInt32() : -1;
            var all = Start(); using var allJson = all.Json;
            var code = allJson.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? error.GetProperty("code").GetString() : null;
            Require(selected.Exit == 0 && count == 2 && all.Exit == 6 && code == "RESOURCE_LIMIT",
                $"enumerate the entire selection before the 100000-slot budget; selectedExit={selected.Exit}, selectedCount={count}, allExit={all.Exit}, allCode={code}");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
