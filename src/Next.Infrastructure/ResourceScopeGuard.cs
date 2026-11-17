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
            for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
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
                if (Read("level.dat") is { } level)
                    ResourceOutputPolicy.ValidateWorldAncestor(Path.GetRelativePath(ancestor.FullName, root).Replace('\\', '/'), generation, level,
                        Read("data/minecraft/world_gen_settings.dat"));
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }
    public void Dispose() { for (var index = held.Count - 1; index >= 0; index--) held[index].Dispose(); held.Clear(); }
}
