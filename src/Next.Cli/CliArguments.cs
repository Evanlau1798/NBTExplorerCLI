using System.Globalization;

namespace Next.Cli;

internal sealed class CliFailure(string code, string? details = null, object? data = null) : Exception(details ?? code)
{
    internal string Code { get; } = code;
    internal object? Result { get; } = data;
}
internal sealed record CliArguments(string Command, string? Input, IReadOnlyDictionary<string, string?> Options, string Locale, string? Subcommand = null)
{
    internal bool LegacyBatchPreview => Command == "batch" && Flag("legacy") && !Flag("apply");
    internal static readonly string[] Commands = ["help", "version", "get", "list", "search", "scan", "extract", "validate", "capabilities", "set", "batch", "plan", "apply", "recover", "status", "backup", "restore", "convert", "translate", "agent", "import-legacy"];
    internal static string Canonical(string name) => name switch { "ls" => "list", "find" => "search", "scan-world" => "scan", "extract-text" => "extract", "translate-session" => "translate", "--help" or "-h" => "help", "--version" or "-v" => "version", _ => name };
    internal static string[] Allowed(string command, string? subcommand = null) => command switch
    {
        "get" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "help"],
        "list" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "limit", "offset", "help"],
        "search" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "limit", "offset", "query", "name", "type", "help"],
        "scan" => ["path", "dimension", "region", "types", "limit", "offset", "summary", "show-items", "refresh", "no-cache", "cache-root", "workers", "verbose", "debug", "help"],
        "extract" => ["path", "dimension", "region", "types", "limit", "offset", "all", "unique", "match-pattern", "output", "refresh", "no-cache", "cache-root", "workers", "verbose", "debug", "help"],
        "validate" => ["path", "dimension", "region", "types", "limit", "offset", "quick", "refresh", "no-cache", "cache-root", "workers", "verbose", "debug", "help"],
        "set" => ["path", "pointer", "segments", "chunk", "dimension", "compression", "value", "value-file", "operation-id", "apply", "world-offline", "state-root", "output", "help"],
        "batch" => ["world", "legacy", "migration-report", "apply", "world-offline", "state-root", "output", "help"],
        "plan" => ["request", "state-root", "output", "help"],
        "apply" => ["plan", "world-offline", "state-root", "output", "help"],
        "recover" => ["plan-hash", "world-offline", "rollback", "state-root", "output", "help"],
        "status" => ["plan-hash", "state-root", "output", "help"],
        "backup" => ["path", "suffix", "state-root", "output", "help"],
        "restore" => ["manifest-hash", "path", "apply", "world-offline", "state-root", "output", "help"],
        "convert" => ["lang", "prefix", "keepdup", "no-backup", "language-output", "apply", "world-offline", "state-root", "output", "path", "dimension", "region", "types", "match-pattern", "workers", "verbose", "help"],
        "translate" => SessionCommands.Allowed(subcommand ?? "help"),
        "agent" => AgentCommands.Allowed(subcommand ?? "help"),
        "import-legacy" => ["input", "profile", "import-id", "list", "source-language", "target-language", "acknowledge-world-rebind", "create-session", "state-root", "output", "limit", "offset", "help"],
        "help" or "version" or "capabilities" => ["help"],
        _ => throw new CliFailure("UNKNOWN_COMMAND")
    };
    internal static string[] ExtractLocale(IReadOnlyList<string> args, out string locale, out string command)
    {
        locale = "zh-TW"; var seen = false; var rest = new List<string>(); var positional = false; CliFailure? failure = null;
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index]; if (arg == "--") positional = true;
            if (!positional && (arg == "--ui-locale" || arg.StartsWith("--ui-locale=", StringComparison.Ordinal)))
            {
                if (seen) failure ??= new("INVALID_ARGUMENTS", "Repeated --ui-locale"); seen = true;
                var value = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : ++index < args.Count ? args[index] : "";
                var parsed = value.ToLowerInvariant() switch { "en-us" => "en-US", "zh-tw" => "zh-TW", _ => null };
                if (parsed is null) failure ??= new("INVALID_ARGUMENTS", "Use --ui-locale zh-TW or en-US"); else locale = parsed;
            }
            else rest.Add(arg);
        }
        command = Canonical(rest.Count == 0 ? "help" : rest[0]);
        if (failure is not null) throw failure;
        return rest.ToArray();
    }
    internal static CliArguments Parse(string[] args, string locale)
    {
        var command = Canonical(args.Length == 0 ? "help" : args[0]); string? subcommand = null;
        if (command is "translate" or "agent")
        {
            subcommand = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "help";
            if (args.Length > 1 && !args[1].StartsWith('-')) args = [args[0], .. args.Skip(2)];
        }
        var allowed = Allowed(command, subcommand).ToHashSet(StringComparer.Ordinal);
        var options = new Dictionary<string, string?>(StringComparer.Ordinal); var inputs = new List<string>(); var positional = false;
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (!positional && arg == "--") { positional = true; continue; }
            if (!positional && arg != "-" && arg.StartsWith('-'))
            {
                var separator = arg.IndexOf('='); var name = separator < 0 ? arg : arg[..separator];
                name = name switch { "--q" or "-q" => "query", "-h" => "help", "-k" when command == "convert" => "keepdup", "-v" when command == "convert" => "verbose", _ when name.StartsWith("--", StringComparison.Ordinal) => name[2..], _ => throw new CliFailure("UNKNOWN_OPTION", arg) };
                if (command == "scan") name = name switch { "summary-only" => "summary", "items" => "show-items", _ => name };
                if (command == "extract") name = name switch { "no-limit" => "all", "deduplicate" => "unique", _ => name };
                if (!allowed.Contains(name)) throw new CliFailure("UNKNOWN_OPTION", arg);
                string? value = null;
                if (name == "types" && command is "get" or "list" or "search" || name is "help" or "summary" or "show-items" or "refresh" or "no-cache" or "all" or "unique" or "quick" or "verbose" or "debug" or "apply" or "world-offline" or "rollback" or "standalone" or "no-group" or "confirm" or "acknowledge-world-rebind" or "create-session" or "list" or "direct" or "keys" or "synchronize-references" or "legacy" or "keepdup" or "no-backup") { if (separator >= 0) throw new CliFailure("INVALID_ARGUMENTS", "Boolean flags take no value"); }
                else
                {
                    value = separator >= 0 ? arg[(separator + 1)..] : ++index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal) ? args[index] : throw new CliFailure("INVALID_ARGUMENTS", "Missing option value");
                    if (value.Length == 0 && name is not ("pointer" or "path")) throw new CliFailure("INVALID_ARGUMENTS", "Empty option value");
                }
                if (!options.TryAdd(name, value)) throw new CliFailure("INVALID_ARGUMENTS", "Repeated option: " + name);
            }
            else inputs.Add(arg);
        }
        if (inputs.Count > 1 || command is "version" or "capabilities" && inputs.Count != 0 || command is not ("help" or "version" or "capabilities") && subcommand != "help" && inputs.Count == 0 && !options.ContainsKey("help")) throw new CliFailure("INVALID_ARGUMENTS", "Supply exactly one input path");
        if (options.ContainsKey("pointer") && options.ContainsKey("segments")) throw new CliFailure("INVALID_ARGUMENTS", "Choose one exact path format");
        var result = new CliArguments(command, inputs.SingleOrDefault(), options, locale, subcommand);
        _ = result.Number("limit", 100, 1, 10_000); _ = result.Number("offset", 0, 0, 2_000_000);
        if (result.Get("compression") is { } codec && codec is not ("auto" or "raw" or "gzip" or "zlib")) throw new CliFailure("INVALID_ARGUMENTS", "Unknown compression");
        if (command == "search" && !options.ContainsKey("help") && !new[] { "query", "name", "type" }.Any(name => options.ContainsKey(name))) throw new CliFailure("INVALID_ARGUMENTS", "Search needs query, name or type");
        return result;
    }
    internal string? Get(string name) => Options.GetValueOrDefault(name);
    internal bool Flag(string name) => Options.ContainsKey(name);
    internal int Number(string name, int fallback, int minimum, int maximum)
    {
        if (Get(name) is not { } value) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number : throw new CliFailure("INVALID_ARGUMENTS", "Out of range --" + name);
    }
}
