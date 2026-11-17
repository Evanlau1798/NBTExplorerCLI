using System.Globalization;

namespace Next.Cli;

internal sealed class CliFailure(string code, string? details = null) : Exception(details ?? code)
{
    internal string Code { get; } = code;
}
internal sealed record CliArguments(string Command, string? Input, IReadOnlyDictionary<string, string?> Options, string Locale)
{
    internal static readonly string[] Commands = ["help", "version", "get", "list", "search"];
    internal static string Canonical(string name) => name switch { "ls" => "list", "find" => "search", "--help" or "-h" => "help", "--version" or "-v" => "version", _ => name };
    internal static string[] Allowed(string command) => command switch
    {
        "get" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "help"],
        "list" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "limit", "offset", "help"],
        "search" => ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "types", "limit", "offset", "query", "name", "type", "help"],
        "help" or "version" => ["help"],
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
        var command = Canonical(args.Length == 0 ? "help" : args[0]); var allowed = Allowed(command).ToHashSet(StringComparer.Ordinal);
        var options = new Dictionary<string, string?>(StringComparer.Ordinal); var inputs = new List<string>(); var positional = false;
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (!positional && arg == "--") { positional = true; continue; }
            if (!positional && arg.StartsWith('-'))
            {
                var separator = arg.IndexOf('='); var name = separator < 0 ? arg : arg[..separator];
                name = name switch { "--q" or "-q" => "query", "-h" => "help", _ when name.StartsWith("--", StringComparison.Ordinal) => name[2..], _ => throw new CliFailure("UNKNOWN_OPTION", arg) };
                if (!allowed.Contains(name)) throw new CliFailure("UNKNOWN_OPTION", arg);
                string? value = null;
                if (name is "types" or "help") { if (separator >= 0) throw new CliFailure("INVALID_ARGUMENTS", "Boolean flags take no value"); }
                else
                {
                    value = separator >= 0 ? arg[(separator + 1)..] : ++index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal) ? args[index] : throw new CliFailure("INVALID_ARGUMENTS", "Missing option value");
                    if (value.Length == 0 && name is not ("pointer" or "path")) throw new CliFailure("INVALID_ARGUMENTS", "Empty option value");
                }
                if (!options.TryAdd(name, value)) throw new CliFailure("INVALID_ARGUMENTS", "Repeated option: " + name);
            }
            else inputs.Add(arg);
        }
        if (inputs.Count > 1 || command == "version" && inputs.Count != 0 || command is not ("help" or "version") && inputs.Count == 0 && !options.ContainsKey("help")) throw new CliFailure("INVALID_ARGUMENTS", "Supply exactly one input path");
        if (options.ContainsKey("pointer") && options.ContainsKey("segments")) throw new CliFailure("INVALID_ARGUMENTS", "Choose one exact path format");
        var result = new CliArguments(command, inputs.SingleOrDefault(), options, locale);
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
