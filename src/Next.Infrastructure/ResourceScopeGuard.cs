using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

internal sealed class ResourceScopeGuard : IDisposable
{
    private readonly List<IDisposable> held = [];
    internal static IDisposable Acquire(string root, ResourceGeneration generation)
    {
        var guard = new ResourceScopeGuard();
        try
        {
            var rootLease = GuardedArtifactPath.AcquireDirectory(root, ""); guard.held.Add(rootLease);
            void Inspect(DirectoryInfo ancestor, string levelName = "level.dat", bool required = false)
            {
                byte[]? Read(string relative)
                {
                    try
                    {
                        if (Path.GetDirectoryName(relative) is { Length: > 0 } directory)
                            rootLease.HoldExistingBranch(ancestor.FullName, directory.Replace('\\', '/'));
                        var stream = GuardedArtifactPath.OpenFile(Path.Combine(ancestor.FullName, relative), FileMode.Open, FileAccess.Read, FileShare.Read);
                        guard.held.Add(stream);
                        if (stream.Length > ArtifactStorage.MaximumBytes) throw new ArtifactSizeLimitException();
                        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
                    }
                    catch (FileNotFoundException) { return null; }
                }
                if (Read(levelName) is { } level)
                    ResourceOutputPolicy.ValidateWorldAncestor(generation.Outputs.Select(output => Path.GetRelativePath(ancestor.FullName,
                        Path.Combine(root, output.RelativePath)).Replace('\\', '/')).Where(path => path != ".." && !path.StartsWith("../", StringComparison.Ordinal)),
                        level, Read("data/minecraft/world_gen_settings.dat"));
                else if (required) throw new ArtifactConflictException("Descendant world metadata disappeared during scope inspection");
            }
            for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent) Inspect(ancestor);
            var paths = generation.Outputs.Select(output => output.RelativePath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Any(path => path.Contains('/')))
            {
                var catalog = new WorldInventory(root, generation.WorldId).Enumerate();
                if (!catalog.Complete) throw new IOException("Resource descendant scope catalog is incomplete");
                foreach (var marker in catalog.Entries.Select(entry => entry.RelativePath).Concat(catalog.DirectoryPaths)
                    .Where(path => path.EndsWith("/level.dat", StringComparison.OrdinalIgnoreCase)))
                {
                    var directory = marker[..marker.LastIndexOf('/')]; var prefix = directory + "/";
                    var index = Array.BinarySearch(paths, prefix, StringComparer.OrdinalIgnoreCase); if (index < 0) index = ~index;
                    if (index == paths.Length || !paths[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    rootLease.HoldExistingBranch(root, directory);
                    Inspect(new DirectoryInfo(Path.Combine(root, directory)), Path.GetFileName(marker), required: true);
                }
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }
    public void Dispose() { for (var index = held.Count - 1; index >= 0; index--) held[index].Dispose(); held.Clear(); }
}
