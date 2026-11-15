using Next.Domain;

namespace Next.Minecraft;

public static class WorldSourceScope
{
    public static bool Includes(WorldSourceLocation source, SnapshotScope scope)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(scope);
        if (!scope.ArtifactPaths.IsEmpty && !scope.ArtifactPaths.Contains(source.Artifact.RelativePath, StringComparer.Ordinal)) return false;
        if (source.Dimension is not null && !scope.Dimensions.IsEmpty && !scope.Dimensions.Contains(source.Dimension, StringComparer.Ordinal)) return false;
        if (source.RegionX is not null && !scope.Regions.IsEmpty)
        {
            var filename = source.Artifact.RelativePath[(source.Artifact.RelativePath.LastIndexOf('/') + 1)..];
            if (!scope.Regions.Contains(filename, StringComparer.Ordinal) && !scope.Regions.Contains(source.Artifact.RelativePath, StringComparer.Ordinal)) return false;
        }
        return true;
    }
}
