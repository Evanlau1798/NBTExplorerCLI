using System.Reflection;
using System.Text.Json;
using Next.Application;
using Next.Cli;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CliProtocolTests
{
    private static JsonDocument Invoke(string[] arguments, int expectedExit)
    {
        using var output = new StringWriter(); var exit = CliApplication.Run(arguments, output); var raw = output.ToString();
        Require(exit == expectedExit, "contract exit differs for " + string.Join(' ', arguments) + ": " + raw);
        Require(raw.EndsWith(Environment.NewLine, StringComparison.Ordinal) && raw.TrimEnd('\r', '\n').IndexOfAny(['\r', '\n']) < 0, "stdout is exactly one newline-delimited envelope");
        var result = JsonDocument.Parse(raw); var envelope = result.RootElement;
        Require(envelope.GetProperty("schemaFamily").GetString() == "nbtcli.next.cli" && envelope.GetProperty("schemaVersion").GetInt32() == 1
            && envelope.GetProperty("success").GetBoolean() == (expectedExit == 0), "versioned machine envelope agrees with actual exit");
        return result;
    }

    public static void EveryRegisteredSurfaceHasMatchingHelpAndLocaleStableErrors()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CliContract", "cli-command-surface.json")));
        var entries = contract.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        VerifyAliases(contract.RootElement, entries);
        using var root = Invoke(["help"], 0);
        Require(root.RootElement.GetProperty("data").GetProperty("commands").EnumerateArray().Select(value => value.GetString())
            .SequenceEqual(entries.Where(item => item.GetProperty("subcommand").ValueKind == JsonValueKind.Null).Select(item => item.GetProperty("command").GetString())), "all registered commands have a frozen contract entry");
        foreach (var entry in entries)
        {
            var command = entry.GetProperty("command").GetString()!; var subcommand = entry.GetProperty("subcommand").GetString();
            string[] prefix = subcommand is null ? [command] : [command, subcommand];
            string[] help = command == "help" ? ["help", "help", "--help"] : [.. prefix, "--help"];
            using var zhHelp = Invoke(help, 0);
            using var enHelp = Invoke(["--ui-locale", "en-US", .. help], 0);
            Require(zhHelp.RootElement.GetProperty("data").GetRawText() == enHelp.RootElement.GetProperty("data").GetRawText(), "locale cannot change machine help/options");
            var data = zhHelp.RootElement.GetProperty("data");
            Require(data.GetProperty("options").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(entry.GetProperty("options").EnumerateArray().Select(value => value.GetString())), "documented options match actual help: " + string.Join(' ', prefix));
            if (subcommand is null && command is "translate" or "agent")
                Require(data.GetProperty("subcommands").EnumerateArray().Select(value => value.GetString()).SequenceEqual(entries
                    .Where(item => item.GetProperty("command").GetString() == command && item.GetProperty("subcommand").ValueKind != JsonValueKind.Null)
                    .Select(item => item.GetProperty("subcommand").GetString())), "all registered actions have contract entries");
            using var zhError = Invoke([.. prefix, "--g06-unknown"], 2);
            using var enError = Invoke([.. prefix, "--g06-unknown", "--ui-locale=en-US"], 2);
            foreach (var key in new[] { "command", "outcome", "data" })
                Require(zhError.RootElement.GetProperty(key).GetRawText() == enError.RootElement.GetProperty(key).GetRawText(), "error machine fields are locale independent");
            var z = zhError.RootElement.GetProperty("error"); var e = enError.RootElement.GetProperty("error");
            Require(z.GetProperty("code").GetString() == "UNKNOWN_OPTION" && z.GetProperty("code").GetRawText() == e.GetProperty("code").GetRawText()
                && z.GetProperty("message").GetString() != e.GetProperty("message").GetString(), "each command reports localized errors with stable codes");
            using var repeated = Invoke([.. prefix, "--help", "--help"], 2);
            Require(repeated.RootElement.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENTS", "duplicate help fails before any command side effect");
        }
    }

    private static void VerifyAliases(JsonElement contract, JsonElement[] entries)
    {
        foreach (var alias in contract.GetProperty("rootAliases").EnumerateObject())
        {
            var canonical = alias.Value.GetString()!;
            if (canonical is "help" or "version")
            {
                using var output = Invoke([alias.Name], 0);
                Require(output.RootElement.GetProperty("command").GetString() == canonical, "root shorthand emits its canonical command");
            }
            else
            {
                using var output = Invoke([alias.Name, "--g06-unknown"], 2);
                Require(output.RootElement.GetProperty("command").GetString() == canonical, "legacy alias error retains canonical command");
                using var help = Invoke(["help", alias.Name], 0);
                Require(help.RootElement.GetProperty("data").GetProperty("subject").GetString() == canonical, "legacy alias shares canonical help");
            }
        }
        var parse = typeof(CliApplication).Assembly.GetType("Next.Cli.CliArguments")!.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var alias in contract.GetProperty("optionAliases").EnumerateArray())
        {
            var commands = alias.GetProperty("commands").EnumerateArray().Select(item => item.GetString()).ToArray();
            var actions = alias.GetProperty("subcommands"); var option = alias.GetProperty("option").GetString()!;
            foreach (var entry in entries.Where(item => commands.Contains("*") || commands.Contains(item.GetProperty("command").GetString())))
            {
                var command = entry.GetProperty("command").GetString()!; var action = entry.GetProperty("subcommand").GetString();
                if (actions.ValueKind != JsonValueKind.Null && !actions.EnumerateArray().Any(item => item.GetString() == action)) continue;
                string[] prefix = action is null ? [command] : [command, action];
                string[] input = command is "help" or "version" or "capabilities" || command is "translate" or "agent" && action is null or "help" ? [] : ["placeholder"];
                string[] value = alias.GetProperty("kind").GetString() == "value" ? ["probe"] : [];
                string[] arguments = [.. prefix, .. input, alias.GetProperty("alias").GetString()!, .. value];
                object result;
                try { result = parse.Invoke(null, [arguments, "zh-TW"])!; }
                catch (TargetInvocationException error) { throw new InvalidOperationException("Alias contract parse failed: " + string.Join(' ', arguments), error.InnerException); }
                var options = (IReadOnlyDictionary<string, string?>)result.GetType().GetProperty("Options")!.GetValue(result)!;
                Require(options.TryGetValue(option, out var accepted) && accepted == value.SingleOrDefault(), "documented scoped flag alias reaches its declared parser option");
            }
        }
    }

    public static void NineReceiptOutcomesKeepCountsAndExitClassification()
    {
        var owner = typeof(CliApplication).Assembly.GetType("Next.Cli.WriteCommands")!;
        var status = owner.GetMethod("Status", BindingFlags.NonPublic | BindingFlags.Static)!;
        var hash = Hash256.Compute("protocol-contract"u8); var artifact = new ArtifactAddress(Guid.NewGuid(), "data/value.dat", ArtifactKind.Nbt);
        var image = ArtifactImage.Present(hash);
        var expected = new[] { 0, 0, 7, 5, 3, 4, 130, 7, 8 };
        OperationReceipt Item(OperationOutcome outcome, string id) => new(id, outcome, "observed", outcome switch
        {
            OperationOutcome.Applied or OperationOutcome.Unchanged => new ImageProof(ImageProofKind.Postimage, artifact, image, image),
            OperationOutcome.RolledBack => new ImageProof(ImageProofKind.Preimage, artifact, image, image), _ => null
        });
        void Check(ApplyReceipt receipt, ApplyJournalPhase phase, ApplyDirection direction, int exit)
        {
            var state = new WorldApplyStatus(hash, direction, phase, 7, hash, receipt);
            var result = status.Invoke(null, [state, null])!; var type = result.GetType();
            Require((int)type.GetProperty("Exit")!.GetValue(result)! == exit, "receipt classification retains declared exit for " + string.Join(',', receipt.Results.Select(item => item.Outcome)));
            var code = (string?)type.GetProperty("Code")!.GetValue(result); var outcome = (string)type.GetProperty("Outcome")!.GetValue(result)!;
            var json = CommandOutputJson.Write("status", type.GetProperty("Data")!.GetValue(result), outcome, code);
            using var output = JsonDocument.Parse(json); var encoded = output.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data");
            Require(output.RootElement.GetProperty("success").GetBoolean() == (exit == 0) && encoded.GetProperty("counts").GetArrayLength() == 9
                && encoded.GetProperty("counts").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()) == receipt.Requested
                && encoded.GetProperty("requested").GetInt32() == receipt.Requested && encoded.GetProperty("revision").GetInt64() == receipt.Revision,
                "CLI conserves every operation once and retains immutable receipt revision");
            Require(encoded.GetProperty("results").GetArrayLength() == receipt.Requested, "complete per-operation outcomes survive the envelope");
        }
        foreach (var outcome in Enum.GetValues<OperationOutcome>())
        {
            var terminal = outcome is OperationOutcome.Applied or OperationOutcome.Unchanged or OperationOutcome.RolledBack;
            var receipt = new ApplyReceipt(hash, 7, ["one"], [Item(outcome, "one")], terminal);
            Check(receipt, terminal ? ApplyJournalPhase.Completed : ApplyJournalPhase.RecoveryRequired,
                outcome == OperationOutcome.RolledBack ? ApplyDirection.Rollback : ApplyDirection.Forward, expected[(int)outcome]);
        }
        var all = Enum.GetValues<OperationOutcome>().Select(outcome => Item(outcome, outcome.ToString())).ToArray();
        var mixed = new ApplyReceipt(hash, 8, all.Select(item => item.OperationId), all, false);
        Check(mixed, ApplyJournalPhase.RecoveryRequired, ApplyDirection.Forward, 7);
        Check(mixed, ApplyJournalPhase.RecoveryRequired, ApplyDirection.Forward, 7);
        var pending = new ApplyReceipt(hash, 9, ["pending"], [Item(OperationOutcome.Applied, "pending")], false);
        Check(pending, ApplyJournalPhase.RecoveryRequired, ApplyDirection.Forward, 7);
    }

    public static void MissingMessageKeysHaveSafeLocalizedFallbacks()
    {
        var message = typeof(CliApplication).GetMethod("Message", BindingFlags.NonPublic | BindingFlags.Static)!;
        Require((string)message.Invoke(null, ["FUTURE_CODE", "zh-TW"])! == "操作失敗。"
            && (string)message.Invoke(null, ["FUTURE_CODE", "en-US"])! == "The operation failed.", "unknown machine codes retain a localized fallback rather than exposing a missing key");
    }
}
