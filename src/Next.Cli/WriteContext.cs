using Next.Application;
using Next.Domain;
using Next.Infrastructure;

namespace Next.Cli;

internal sealed class WriteContext
{
    internal string Root { get; }
    internal string State { get; }
    internal WorldInventory Inventory { get; }
    internal ArtifactStorage Storage { get; }
    internal WriteContext(string root, CliArguments request)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException("World directory not found");
        Inventory = QuerySource.Inventory(Root); Storage = new(Root, Inventory.WorldId);
        State = Path.GetFullPath(request.Get("state-root") ?? Path.Combine(StatePaths.DataRoot, "worlds", Inventory.WorldId.ToString("N")));
        ScanCommands.OutsideWorld(Root, State);
        if (request.Get("output") is { } output) { ScanCommands.OutsideWorld(Root, Path.GetFullPath(output)); QueryExportFile.EnsureNew(output); }
    }
    internal IWorldBackupStore Objects() => new FileSystemWorldBackupStore(Path.Combine(State, "objects"));
    internal WorldApplyEngine Engine() => new(Inventory, Storage, Objects(), new FileSystemApplyJournalStore(Path.Combine(State, "journals")));
    internal WorldQuerySnapshot Query() => new WorldQueryService(Inventory, new FileSystemScanCacheStore(Path.Combine(State, "cache")))
        .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot ?? throw new CliFailure("INCOMPLETE_SOURCE", "World query was interrupted");
    internal ArtifactImage Image(ArtifactAddress artifact)
    {
        try { return ArtifactImage.Present(Hash256.Compute(Inventory.Read(artifact))); }
        catch (FileNotFoundException) { return ArtifactImage.Absent; }
    }
    internal ArtifactAddress Artifact(string relative)
    {
        var kind = Path.GetExtension(relative).ToLowerInvariant() switch { ".json" => ArtifactKind.Json, ".zip" => ArtifactKind.Archive,
            ".mca" or ".mcr" => relative.StartsWith("entities/", StringComparison.Ordinal) || relative.Contains("/entities/", StringComparison.Ordinal) ? ArtifactKind.Entities : ArtifactKind.Region,
            ".mcfunction" => ArtifactKind.McFunction, _ => Path.GetFileName(relative) == "pack.mcmeta" ? ArtifactKind.Json : ArtifactKind.Nbt };
        return new(Inventory.WorldId, relative, kind);
    }
}
