using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

public sealed class WorldInventory : IWorldInventory, IResourceOutputScope
{
    private readonly string root;
    private readonly ArtifactStorage storage;
    public Guid WorldId { get; }
    public Hash256 AccessBinding => storage.AccessBinding;
    public WorldInventory(string root, Guid worldId)
    {
        storage = new(root, worldId); this.root = Path.GetFullPath(root); WorldId = worldId;
    }
    public WorldInventorySnapshot Enumerate(int maxEntries = 100_000)
    {
        if (maxEntries is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var entries = new List<WorldInventoryEntry>(); var pending = new Stack<string>(); pending.Push("");
        var directoryPaths = new List<string>();
        var directories = 0; long pathBytes = 0;
        WorldInventorySnapshot Finish(string? code = null) => new(entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal), code, directoryPaths.Order(StringComparer.Ordinal));
        bool Add(WorldInventoryEntry entry)
        {
            if (entries.Count == maxEntries) return false;
            entries.Add(entry); return true;
        }
        while (pending.TryPop(out var directory))
        {
            if (++directories > 100_000) return Finish("DirectoryLimitExceeded");
            try
            {
                using var lease = GuardedArtifactPath.AcquireDirectory(root, directory);
                foreach (var info in new DirectoryInfo(lease.Target).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                {
                    var relative = directory.Length == 0 ? info.Name : directory + "/" + info.Name;
                    pathBytes = checked(pathBytes + (long)relative.Length * 2);
                    if (pathBytes > 67_108_864) return Finish("CatalogPathBytesLimit");
                    try
                    {
                        var attributes = info.Attributes; var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            if (!Add(new(relative, 0, isDirectory, SourceOutcome.Unsupported, "ReparsePoint"))) return Finish("CatalogLimitExceeded");
                        }
                        else if (isDirectory)
                        {
                            if (relative.Count(character => character == '/') >= 256)
                            {
                                if (!Add(new(relative, 0, true, SourceOutcome.Failed, "DirectoryDepthLimit"))) return Finish("CatalogLimitExceeded");
                            }
                            else
                            {
                                if (pending.Count + directories >= 100_000) return Finish("DirectoryLimitExceeded");
                                directoryPaths.Add(relative);
                                pending.Push(relative);
                            }
                        }
                        else
                        {
                            var bytes = ((FileInfo)info).Length;
                            if (!Add(new(relative, bytes, false, bytes <= ArtifactStorage.MaximumBytes ? SourceOutcome.Success : SourceOutcome.Failed,
                                bytes <= ArtifactStorage.MaximumBytes ? null : "ArtifactSizeLimit"))) return Finish("CatalogLimitExceeded");
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        if (!Add(new(relative, 0, false, SourceOutcome.Failed, "MetadataReadFailed"))) return Finish("CatalogLimitExceeded");
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (directory.Length == 0) return Finish(error is NotSupportedException ? "UnsupportedFilesystem" : "RootEnumerationFailed");
                if (!Add(new(directory, 0, true, error is NotSupportedException ? SourceOutcome.Unsupported : SourceOutcome.Failed, "DirectoryEnumerationFailed"))) return Finish("CatalogLimitExceeded");
            }
        }
        return Finish();
    }
    public byte[] Read(ArtifactAddress artifact) => storage.Read(artifact);
    public IDisposable GuardResourceOutput(ResourceGeneration generation) => ResourceScopeGuard.Acquire(root, generation);
}
