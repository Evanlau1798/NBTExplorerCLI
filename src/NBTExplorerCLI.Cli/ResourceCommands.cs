using System.Globalization;
using System.Collections.Immutable;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal static class ResourceCommands
{
    internal static CliResult Execute(CliArguments request)
    {
        var write = request.Flag("apply");
        if (write && !request.Flag("world-offline")) throw new CliFailure("INVALID_ARGUMENTS", "Writing requires --world-offline after closing Minecraft and other writers");
        if (!Guid.TryParse(request.Get("session-id"), out var id) || id == Guid.Empty
            || !long.TryParse(request.Get("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            throw new CliFailure("INVALID_ARGUMENTS", "Supply an exact --session-id and nonnegative --revision");
        var cleanOptions = request.Options.Where(option => option.Key is not ("output" or "plan-output")).ToDictionary();
        var source = new WriteContext(request.Input!, request with { Options = cleanOptions });
        var mergePath = request.Get("merge") is { } merge ? Path.GetFullPath(merge) : null;
        if (mergePath is not null && Directory.Exists(mergePath)) ScanCommands.OutsideWorld(mergePath, source.State);
        var session = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(source.State, "sessions"))).Read(id);
        if (session.Source.WorldId != source.Inventory.WorldId || session.Revision != revision
            || session.Slots.Any(slot => slot.State is not (SessionSlotState.Translated or SessionSlotState.Unchanged or SessionSlotState.Applied)))
            throw new CliFailure("CONFLICT", "Retain this session and inspect its world binding, revision and pending replies before exporting resources");
        var originalLocale = Locale(request.Get("original-lang") ?? session.SourceLocale);
        if (originalLocale == TranslationLanguages.Undetermined)
            throw new CliFailure("INVALID_ARGUMENTS", "The original language is undetermined. Supply --original-lang <language>.");
        var targetLocale = Locale(request.Get("target-lang") ?? session.TargetLocale);
        if (targetLocale == TranslationLanguages.Undetermined)
            throw new CliFailure("INVALID_ARGUMENTS", "The translated language is undetermined. Supply --target-lang <language>.");
        if (originalLocale == targetLocale) throw new CliFailure("INVALID_ARGUMENTS", "Original and translated resource locales must be distinct");
        var pack = request.Subcommand == "create-pack";
        var metadata = pack && (!request.Flag("output") || !request.Flag("pack-format")) ? Metadata(source) : null;
        var outputRoot = Path.GetFullPath(request.Get("output") ?? (pack
            ? metadata!.Profile.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced" ? Path.Combine(source.Root, "resourcepacks") : source.Root
            : Path.Combine(source.Root, ".nbtcli.language")));
        var targetOptions = new Dictionary<string, string?>(cleanOptions) { ["state-root"] = source.State };
        if (File.Exists(outputRoot)) throw new CliFailure("INVALID_ARGUMENTS", "Resource --output requires a directory");
        var anchor = outputRoot;
        while (!Directory.Exists(anchor)) anchor = Path.GetDirectoryName(anchor) ?? throw new DirectoryNotFoundException("No existing physical output ancestor");
        var relative = Path.GetRelativePath(anchor, outputRoot).Replace('\\', '/'); var placement = relative == "." ? "" : relative;
        var target = new WriteContext(anchor, request with { Input = anchor, Options = targetOptions });
        if (request.Get("plan-output") is { } planOutput)
        {
            var file = Path.GetFullPath(planOutput); ScanCommands.OutsideWorld(source.Root, file); ScanCommands.OutsideWorld(target.Root, file);
            if (mergePath is not null && Directory.Exists(mergePath)) ScanCommands.OutsideWorld(mergePath, file);
            QueryExportFile.EnsureNew(file);
        }
        var prefix = request.Get("prefix") ?? "map.";
        if (prefix.Length is < 1 or > 128 || !prefix.All(unit => char.IsAsciiLetterOrDigit(unit) || unit is '.' or '_' or '-')) throw new CliFailure("INVALID_ARGUMENTS", "Key prefix requires 1-128 ASCII resource characters");
        if (session.Slots.Length > 50_000) throw new CodecException(CodecError.LimitExceeded, 0, "Two-locale resource output exceeds the language entry budget");
        ResourceLanguage Language(string locale, bool original) => new(locale, session.Slots.Select(slot => new ResourceLanguageEntry(prefix + slot.Slot.Id.Digest.Hex,
            original ? slot.Slot.SourceText : slot.Reply?.Text ?? slot.Slot.SourceText)).ToImmutableArray());
        var formatName = request.Get("format") ?? (request.Flag("name") ? "directory" : "zip");
        if (pack && formatName is not ("zip" or "directory")) throw new CliFailure("INVALID_ARGUMENTS", "Pack format must be zip or directory");
        var format = !pack ? ResourceOutputFormat.LanguageFiles : formatName == "zip" ? ResourceOutputFormat.ZipPack : ResourceOutputFormat.DirectoryPack;
        var name = pack ? request.Get("name") ?? "resources" : "";
        if (format == ResourceOutputFormat.ZipPack && !name.EndsWith(".zip", StringComparison.Ordinal)) name += ".zip";
        if (placement.Length != 0) name = pack ? placement + "/" + name : placement;
        var packFormat = pack ? request.Get("pack-format") is { } declared ? PackFormat(declared) : MinecraftProfiles.RequiredResourcePackFormat(metadata!.DataVersion) : null;
        var objects = target.Objects();
        var recipe = WriteCommands.BeforeCommit(["resources"], "PlanRejected", () =>
        {
            var result = new ResourceGeneration(target.Inventory.WorldId, format, name, [Language(originalLocale, true), Language(targetLocale, false)], packFormat);
            if (metadata is null && session.Source.Sources.Any(input => input.Artifact.RelativePath == "level.dat")
                && result.Outputs.Any(output => Path.GetFullPath(Path.Combine(target.Root, output.RelativePath)).StartsWith(
                    Path.EndsInDirectorySeparator(source.Root) ? source.Root : source.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                _ = Metadata(source);
            if (mergePath is null) return result;
            var directory = Directory.Exists(mergePath); var input = QuerySource.Inventory(directory ? mergePath : Path.GetDirectoryName(mergePath)!);
            return new ResourceInputService(objects).Capture(result, input, directory ? null : new(input.WorldId, Path.GetFileName(mergePath), ArtifactKind.Archive));
        });
        var service = new ResourceOutputService(target.Inventory, target.Storage, objects);
        var plan = WriteCommands.BeforeCommit(["resources"], "PlanRejected", () => service.Build(recipe));
        var json = PatchPlanJson.Write(plan);
        object Description(object result) => new { result, sourceSession = new { sessionId = id, revision, sourceSnapshot = session.Source.SnapshotId.Digest, sessionModified = false },
            outputRoot, physicalRoot = target.Root, stateRoot = target.State, planHash = plan.PlanHash, originalLocale, targetLocale };
        if (!write)
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });
            return new(new { preview = true, plan = parsed.RootElement.Clone(), resources = Description(new { format = format.ToString() }),
                commitArguments = new[] { "apply", target.Root, "--plan", request.Get("plan-output") is { } path ? Path.GetFullPath(path) : "<retained-plan>", "--world-offline", "--state-root", target.State } }, Export: json);
        }
        var engine = target.Engine();
        var replay = WriteExecution.TryReplay(engine, plan, target.Root, target.State);
        if (replay is not null) return replay with { Data = Description(replay.Data), Export = json };
        using var prepared = WriteCommands.BeforeCommit(["resources"], "PreparationRejected", () => service.Prepare(plan, true));
        var applied = WriteCommands.Apply(prepared, engine, target.Root, target.State);
        return applied with { Data = Description(applied.Data), Export = json };
    }
    private static string Locale(string value)
    {
        var locale = value.Replace('-', '_').ToLowerInvariant();
        if (locale.Length is < 1 or > 64 || locale is "." or ".." || !locale.All(unit => char.IsAsciiLetterLower(unit) || char.IsAsciiDigit(unit) || unit is '_' or '.'))
            throw new CliFailure("INVALID_ARGUMENTS", "Resource language must be a bounded Minecraft locale");
        return locale;
    }
    private static PackFormatVersion PackFormat(string value)
    {
        var fields = value.Split('.');
        if (fields.Length is < 1 or > 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(fields.Length == 1 ? "0" : fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            throw new CliFailure("INVALID_ARGUMENTS", "Pack format requires major or major.minor");
        return new(major, minor);
    }
    private static WorldMetadata Metadata(WriteContext context)
    {
        var level = CompressedNbt.ReadDocument(context.Inventory.Read(new(context.Inventory.WorldId, "level.dat", ArtifactKind.Nbt)), NbtCompression.GZip).Document;
        NbtDocument? worldGen = null;
        try { worldGen = CompressedNbt.ReadDocument(context.Inventory.Read(new(context.Inventory.WorldId, "data/minecraft/world_gen_settings.dat", ArtifactKind.Nbt)), NbtCompression.GZip).Document; }
        catch (FileNotFoundException) { }
        return WorldMetadata.Read(level, worldGen);
    }
}
