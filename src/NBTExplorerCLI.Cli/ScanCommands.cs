using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal sealed record CliResult(object Data, string? Code = null, string Outcome = "Success", int Exit = 0, string? Export = null);

internal static class ScanCommands
{
    internal static CliResult Execute(CliArguments request)
    {
        if (request.Command == "capabilities")
            return new(new { commands = CliArguments.Commands, profiles = new[] { 3700, 3953, 4325, 4671, 4774 }.Select(version =>
                new { dataVersion = version, profile = MinecraftProfiles.ForDataVersion(version)!.Id,
                    features = MinecraftProfiles.ForDataVersion(version)!.Features.OrderBy(pair => pair.Key).Select(pair => new { facet = pair.Key, pair.Value.Schema, pair.Value.Version, pair.Value.Operations }).ToArray() }).ToArray(),
                storageProfiles = new[] { new { storageVersion = WorldMetadata.ClassicAnvilStorageVersion, profile = MinecraftProfiles.ClassicAnvil.Id, readOnly = true,
                    explicitTranslationVersions = new[] { MinecraftProfiles.ClassicGameVersion } } } });
        var options = new ScanOptions(request); var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Input!));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("World directory not found");
        if (request.Get("output") is { } export) OutsideWorld(root, Path.GetFullPath(export));
        var inventory = QuerySource.Inventory(root);
        if (request.Flag("quick"))
        {
            var catalog = WorldDiscovery.Discover(inventory, new(options.Scope));
            var sources = catalog.Sources.Select(source => new { source.Location, source.Chunk, source.Outcome, source.DiagnosticCode }).Skip(options.Offset).Take(options.Limit).ToArray();
            var resource = ReadCommands.IsResourceLimit(catalog.DiagnosticCode) || catalog.Sources.Any(source => ReadCommands.IsResourceLimit(source.DiagnosticCode));
            return ScanProjection.Classify(new { validationScope = "catalog", catalogComplete = catalog.Complete, payloadValidated = false, referenceCoverage = "not-assessed", catalog.DiagnosticCode, sources,
                pagination = new { offset = options.Offset, limit = options.Limit, total = catalog.Sources.Length } }, catalog.Complete, resource,
                ScanProjection.ReadFailure(catalog.DiagnosticCode) || catalog.Sources.Any(source => ScanProjection.ReadFailure(source.DiagnosticCode)),
                catalog.Sources.Any(source => source.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled), catalog.Sources.Any(source => source.Outcome == SourceOutcome.Unsupported));
        }
        var transient = request.Flag("no-cache");
        if (transient && request.Flag("cache-root")) throw new CliFailure("INVALID_ARGUMENTS", "--no-cache cannot select a persistent repository");
        var cache = transient ? Path.Combine(StatePaths.TemporaryRoot, "query-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(request.Get("cache-root") ?? Path.Combine(StatePaths.DataRoot, "scan", inventory.WorldId.ToString("N")));
        OutsideWorld(root, cache);
        using var temporary = transient ? new TemporaryQueryDirectory(cache) : null;
        var store = new FileSystemScanCacheStore(cache);
        var query = new WorldQueryService(inventory, store).QueryAsync(new(new(options.Scope, ClassicVersion: request.Get("minecraft-version")), request.Command == "extract" ? WorldQueryPurpose.Extract : WorldQueryPurpose.Scan,
            new(Workers: options.Workers), ForceFull: transient || request.Flag("refresh"))).GetAwaiter().GetResult();
        var result = ScanProjection.Create(query, options, request);
        if (request.Flag("verbose") || request.Flag("debug")) Console.Error.WriteLine(request.Locale == "en-US" ? "World query completed; inspect coverage and per-source outcomes." : "世界查詢結束；請核對完整性與逐來源結果。");
        return result;
    }
    internal static void OutsideWorld(string world, string destination)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var prefix = Path.EndsInDirectorySeparator(world) ? world : world + Path.DirectorySeparatorChar;
        if (path.Equals(world, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new CliFailure("INVALID_ARGUMENTS", "Tool state and exports must be outside the input world");
    }
}
