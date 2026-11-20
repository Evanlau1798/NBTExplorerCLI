using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Cli;

internal static class FallbackDraftBatch
{
    private const string Family = "nbtcli.translation-draft-batch";
    private static readonly HashSet<string> SelectionOptions = new(
        ["path", "pointer", "segments", "chunk", "dimension", "entry", "compression", "format", "line", "byte-range", "snbt-profile", "text-pointer"],
        StringComparer.Ordinal);

    internal static CliResult Execute(CliArguments request, string file)
    {
        if (!Directory.Exists(request.Input) || !File.Exists(Path.Combine(request.Input!, "level.dat"))
            || request.Options.Keys.Any(key => key is not ("batch" or "source-language" or "target-language" or "session-id" or "state-root" or "output")))
            throw new CliFailure("INVALID_ARGUMENTS", "A fallback batch requires a world directory and per-selection options in its request");
        using var document = BoundedJson.ReadDecoded(CliInput.ReadBytes(file), DecodeLimits.Default);
        var root = document.RootElement;
        if (!Members(root, "schemaFamily", "schemaVersion", "selections") || String(root, "schemaFamily") != Family
            || !root.TryGetNativeProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number) || number != 1
            || !root.TryGetNativeProperty("selections", out var selections) || selections.ValueKind != JsonValueKind.Array)
            throw new CliFailure("INVALID_ARGUMENTS", "Expected a version 1 fallback batch request");
        var count = selections.GetArrayLength();
        if (count is < 1 or > 1024) throw new CliFailure("INVALID_ARGUMENTS", "A fallback batch needs between 1 and 1024 selections");
        var binding = request.Get("session-id") is { } sessionId ? new FallbackSessionBinding(request, sessionId) : null;
        if (binding is null && request.Flag("state-root"))
            throw new CliFailure("INVALID_ARGUMENTS", "A fallback batch state root requires a session ID");

        var drafts = new List<object>(count);
        long bytes = 0;
        string? defaultTarget = null;
        foreach (var item in selections.EnumerateArray())
        {
            if (!Members(item, "sourceHash", "entryHash", "options", "reply")
                || !item.TryGetNativeProperty("options", out var options) || options.ValueKind != JsonValueKind.Object
                || options.EnumerateObject().Any(property => !SelectionOptions.Contains(NativeJsonStrings.PropertyName(property)) || property.Value.ValueKind != JsonValueKind.String)
                || String(options, "path") is null)
                throw new CliFailure("INVALID_ARGUMENTS", "Each batch selection must supply path options and its expected source hash");
            var expectedSource = SourceHash(item, "sourceHash");
            var expectedEntry = options.TryGetNativeProperty("entry", out _) ? SourceHash(item, "entryHash") : null;
            if (expectedEntry is null && item.TryGetNativeProperty("entryHash", out _))
                throw new CliFailure("INVALID_ARGUMENTS", "An entry hash requires an archive entry selection");
            JsonElement? submitted = item.TryGetNativeProperty("reply", out var reply) ? reply : null;
            var args = new List<string> { "translate", "fallback", request.Input! };
            args.AddRange(options.EnumerateObject().Select(property => "--" + NativeJsonStrings.PropertyName(property) + "=" + NativeJsonStrings.Read(property.Value)));
            if ((binding?.Session.SourceLocale ?? request.Get("source-language")) is { } source) args.Add("--source-language=" + source);
            var target = binding?.Session.TargetLocale ?? request.Get("target-language");
            if (target is null && submitted is null) target = defaultTarget ??= TranslationLanguages.Target(null);
            if (target is not null) args.Add("--target-language=" + target);
            var result = FallbackCommands.ExecuteSelection(CliArguments.Parse(args.ToArray()), expectedSource, expectedEntry, submitted, binding);
            bytes += Encoding.UTF8.GetByteCount(CommandOutputJson.Write("translate", result.Data));
            if (bytes > DecodeLimits.Default.MaxDecodedBytes - 65_536)
                throw new CliFailure("RESOURCE_LIMIT", "Fallback batch output exceeds the byte budget");
            drafts.Add(result.Data);
        }
        if (binding is null) return new(new { schemaFamily = Family, schemaVersion = 1, gameplayVerified = false, drafts });
        var session = binding.Session;
        return new(new { schemaFamily = Family, schemaVersion = 1, gameplayVerified = false, drafts,
            session.SessionId, session.Revision, snapshotId = session.Source.SnapshotId.Digest, sessionReplySlots = binding.Replies });
    }

    private static Hash256 SourceHash(JsonElement value, string name) => String(value, name) is { } hash ? new(hash)
        : throw new CliFailure("INVALID_ARGUMENTS", "Each batch selection requires an exact " + name);

    private static string? String(JsonElement value, string name)
        => value.TryGetNativeProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(property) : null;

    private static bool Members(JsonElement value, params string[] members) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().All(property => members.Contains(NativeJsonStrings.PropertyName(property), StringComparer.Ordinal));
}
