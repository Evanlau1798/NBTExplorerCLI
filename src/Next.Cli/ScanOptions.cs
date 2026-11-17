using System.Globalization;
using System.Text.RegularExpressions;
using Next.Domain;

namespace Next.Cli;

internal sealed class ScanOptions
{
    internal SnapshotScope Scope { get; }
    internal Regex? Pattern { get; }
    internal int Offset { get; }
    internal int Limit { get; }
    internal int Workers { get; }
    private static readonly Dictionary<string, string[]> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sign"] = ["sign"], ["HangingSign"] = ["hanging_sign"], ["Book"] = ["book.page"], ["LecternBook"] = ["lectern.book"],
        ["Title"] = ["book.title", "lectern.book.title"], ["Author"] = ["book.author", "lectern.book.author"],
        ["ItemName"] = ["item.name"], ["ItemLore"] = ["item.lore"], ["ArmorStand"] = ["armor_stand.name"], ["EntityName"] = ["entity.name"],
        ["ContainerName"] = ["container.name"], ["CommandBlock"] = ["command.block", "command_block.name"], ["McFunction"] = ["command.function"],
        ["TextDisplay"] = ["text_display.text"], ["BossBar"] = ["bossbar.name"], ["ScoreboardTeam"] = ["scoreboard.team"],
        ["ScoreboardObjective"] = ["scoreboard.objective"], ["Advancement"] = ["advancement"], ["CommandStorage"] = ["command_storage"],
        ["LangKey"] = ["language.value"], ["TranslationKey"] = ["translation.key"]
    };
    private static readonly string[] Roles = [.. Categories.Values.SelectMany(items => items), "sign.front.line", "sign.back.line", "hanging_sign.front.line", "hanging_sign.back.line",
        "scoreboard.team.name", "scoreboard.team.prefix", "scoreboard.team.suffix", "scoreboard.objective.name", "advancement.title", "advancement.description",
        "lectern.book.page", "command_storage.literal", "command_storage.component", "language.key"];
    internal ScanOptions(CliArguments request)
    {
        Offset = request.Number("offset", 0, 0, 2_000_000); Limit = request.Number("limit", 100, 1, 10_000); Workers = request.Number("workers", 2, 1, 8);
        if (request.Flag("summary") && request.Flag("show-items") || request.Flag("all") && (request.Flag("limit") || request.Flag("offset")))
            throw new CliFailure("INVALID_ARGUMENTS", "Choose one pagination or summary mode");
        if (request.Flag("quick") && request.Options.Keys.Any(key => key is "cache-root" or "refresh" or "workers" or "no-cache" or "types"))
            throw new CliFailure("INVALID_ARGUMENTS", "Quick validation only checks the catalog");
        string[] paths = request.Get("path") is { } exact ? [exact] : []; foreach (var path in paths) ArtifactAddress.ValidatePath(path, false);
        var dimensions = Values(request.Get("dimension"));
        if (dimensions.Any(value => !Regex.IsMatch(value, "^[a-z0-9_.-]+:[a-z0-9_./-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || value.Split(':')[1].Split('/').Any(part => part is "" or "." or "..")))
            throw new CliFailure("INVALID_ARGUMENTS", "Use exact namespaced dimensions");
        var types = Values(request.Get("types")).SelectMany(value => Categories.TryGetValue(value, out var roles) ? roles
            : Roles.Any(role => role == value || role.StartsWith(value + ".", StringComparison.Ordinal))
                ? [value] : throw new CliFailure("INVALID_ARGUMENTS", "Unknown text type: " + value)).Distinct(StringComparer.Ordinal).ToArray();
        string[] regions = [];
        if (request.Get("region") is { } region)
        {
            if (region.Contains(','))
            {
                var pair = region.Split(',');
                if (pair.Length != 2 || !int.TryParse(pair[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) || !int.TryParse(pair[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)
                    || x < int.MinValue / 32 || x > int.MaxValue / 32 || z < int.MinValue / 32 || z > int.MaxValue / 32) throw new CliFailure("INVALID_ARGUMENTS", "Region coordinates must be bounded x,z");
                region = "r." + x.ToString(CultureInfo.InvariantCulture) + "." + z.ToString(CultureInfo.InvariantCulture) + ".mca";
            }
            ArtifactAddress.ValidatePath(region, false);
            if (!Regex.IsMatch(Path.GetFileName(region), "^r\\.-?[0-9]+\\.-?[0-9]+\\.mc[ar]$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new CliFailure("INVALID_ARGUMENTS", "Invalid exact region filename");
            regions = [region];
        }
        Scope = new(dimensions, regions, types, paths);
        if (request.Get("match-pattern") is { } pattern)
        {
            try { Pattern = new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException error) { throw new CliFailure("INVALID_ARGUMENTS", "Invalid match pattern: " + error.Message); }
        }
    }
    private static string[] Values(string? value)
    {
        if (value is null) return [];
        var values = value.Split(',').Select(item => item.Trim()).ToArray();
        if (values.Any(string.IsNullOrEmpty)) throw new CliFailure("INVALID_ARGUMENTS", "Empty scope filter");
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal bool Matches(string text)
    {
        try { return Pattern?.IsMatch(text) ?? true; }
        catch (RegexMatchTimeoutException) { throw new CliFailure("PATTERN_TIMEOUT", "Match pattern exceeded 100 ms; narrow the scope or simplify the expression"); }
    }
}
