using System.Globalization;
using System.Text;
using Next.Application;
using Next.Domain;

namespace Next.Cli;

internal static class ConvertCommands
{
    internal static CliResult Execute(CliArguments request)
    {
        var write = request.Flag("apply");
        if (write && !request.Flag("world-offline")) throw new CliFailure("INVALID_ARGUMENTS", "Writing requires --world-offline after closing Minecraft and other writers");
        var locale = request.Get("lang") ?? throw new CliFailure("INVALID_ARGUMENTS", "Conversion requires explicit --lang; UI locale is independent");
        if (locale.Length > 64 || locale is "." or ".." || !locale.All(unit => char.IsAsciiLetterLower(unit) || char.IsAsciiDigit(unit) || unit is '_' or '-' or '.')) throw new CliFailure("INVALID_ARGUMENTS", "Conversion language requires a lowercase resource filename");
        var prefix = request.Get("prefix") ?? "nbt"; if (!prefix.EndsWith('.')) prefix += ".";
        var context = new WriteContext(request.Input!, request); var options = new ScanOptions(request);
        var snapshot = SessionCommands.Query(context, options.Scope, WorldQueryPurpose.Convert, options.Workers);
        var language = context.Language(request.Get("language-output") ?? "resources.zip#assets/nbt/lang/" + locale + ".json");
        var selected = 0; var excluded = 0;
        IEnumerable<LiteralTranslation> Inputs()
        {
            foreach (var source in snapshot.ReadSources())
                foreach (var text in source.Text?.Occurrences ?? [])
                {
                    if (source.Source.Location.Role is WorldSourceRole.Language or WorldSourceRole.PackMetadata) { excluded++; continue; }
                    if (!options.Matches(text.Text)) continue;
                    selected++; var slot = text.ToSlot(locale, locale);
                    yield return new(slot, new(slot.Id, slot.SourceTextHash, locale, slot.SourceText));
                }
        }
        var keys = new WorldKeyConversionService(context.Inventory).Create(snapshot, Inputs(), language, prefix).ToArray();
        if (!request.Flag("keepdup")) keys = keys.Select(key => Rekey(key, prefix)).ToArray();
        var summary = new { language = locale, prefix, keepDuplicates = request.Flag("keepdup"), selected, excludedResourceText = excluded, requested = keys.Length, hasLanguageDependency = keys.Length != 0 };
        Hash256? convenienceBackupHash = null;
        try
        {
            if (write && keys.Length != 0 && !request.Flag("no-backup"))
            {
                var backup = WriteCommands.Execute(new("backup", context.Root, new Dictionary<string, string?> { ["state-root"] = context.State }, request.Locale));
                if (backup.Code is not null) throw new CliFailure(backup.Code, "Convenience backup failed before world commit", backup.Data);
                convenienceBackupHash = Hash256.Compute(new UTF8Encoding(false, true).GetBytes(backup.Export!));
            }
            var result = WriteCommands.ExecuteOperations(context, keys, write, snapshot);
            if (request.Flag("verbose")) Console.Error.WriteLine(request.Locale == "en-US" ? "Conversion finished; inspect the retained plan and operation results." : "轉換結束；請核對保留計畫與逐項結果。");
            return result with { Data = new { result = result.Data, conversion = summary, convenienceBackupHash } };
        }
        catch (CliFailure error)
        { throw new CliFailure(error.Code, error.Message, new { result = error.Result, conversion = summary, convenienceBackupHash, repair = "retain-plan-and-inspect-apply-status" }); }
    }
    private static KeyConversionPatch Rekey(KeyConversionPatch operation, string prefix)
    {
        var conversion = operation.Conversion; var slot = conversion.Slot;
        static string Frame(IEnumerable<string> values) => string.Concat(values.Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));
        var identity = SourceIdentity.Memory(slot.SourceText, slot.SourceLocale, slot.TargetLocale, Frame([slot.Role, slot.Context]),
            TextRevision.Compute(Frame(slot.Placeholders.Tokens.Order(StringComparer.Ordinal))).Hex, "convert-original-v1", conversion.Profile + ":" + conversion.ProfileVersion.ToString(CultureInfo.InvariantCulture));
        return new(operation.Id, new(conversion.Address, conversion.Expected, conversion.Rule, conversion.Profile, conversion.ProfileVersion, slot, conversion.Reply,
            new(conversion.Dependency.LanguageArtifact, conversion.Dependency.PackArtifact, conversion.Dependency.Locale, prefix + identity.Digest.Hex, conversion.Dependency.Text)));
    }
}
