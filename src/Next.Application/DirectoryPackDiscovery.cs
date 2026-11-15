using Next.Domain;
using Next.Minecraft;

namespace Next.Application;

internal static class DirectoryPackDiscovery
{
    internal sealed record State(PackMetadata? Metadata, SourceOutcome Outcome, string? Code, Hash256? ContentHash);
    internal static Dictionary<string, PackSourceKind> FindRoots(WorldInventorySnapshot inventory)
    {
        var roots = new Dictionary<string, PackSourceKind>(StringComparer.Ordinal);
        var paths = inventory.Entries.Select(entry => entry.RelativePath).ToArray();
        foreach (var directory in inventory.DirectoryPaths)
            if (directory.StartsWith("datapacks/", StringComparison.Ordinal) && directory.IndexOf('/', 10) < 0) roots.TryAdd(directory, PackSourceKind.Datapack);
        var assetRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
            for (var index = path.IndexOf("/assets/", StringComparison.Ordinal); index >= 0; index = path.IndexOf("/assets/", index + 1, StringComparison.Ordinal))
                assetRoots.Add(path[..index]);
        foreach (var path in paths)
        {
            if (path.StartsWith("datapacks/", StringComparison.Ordinal))
            {
                var end = path.IndexOf('/', 10);
                if (end >= 0) roots.TryAdd(path[..end], PackSourceKind.Datapack);
            }
            if (!path.EndsWith("/pack.mcmeta", StringComparison.Ordinal)) continue;
            var root = path[..^12];
            if (!roots.ContainsKey(root)) roots.Add(root, assetRoots.Contains(root) ? PackSourceKind.ResourcePack : PackSourceKind.Datapack);
        }
        return roots;
    }
    internal static string? Owner(string path, IReadOnlyDictionary<string, PackSourceKind> roots)
    {
        for (var index = path.LastIndexOf('/'); index >= 0; index = index == 0 ? -1 : path.LastIndexOf('/', index - 1))
            if (roots.ContainsKey(path[..index])) return path[..index];
        return null;
    }
    internal static State Read(IWorldInventory inventory, string root, PackSourceKind kind, DecodeLimits limits)
    {
        Hash256? hash = null;
        try
        {
            var bytes = inventory.Read(new(inventory.WorldId, root + "/pack.mcmeta", ArtifactKind.Json));
            hash = Hash256.Compute(bytes);
            return new(PackMetadata.Read(bytes, kind, limits), SourceOutcome.Success, null, hash);
        }
        catch (Exception error) when (DiscoveryFailure.IsExpected(error))
        {
            var failure = DiscoveryFailure.Describe(error); return new(null, failure.Outcome, "PackMetadata:" + failure.Code, hash);
        }
    }
}
