using System.Collections.Immutable;

namespace Next.Domain;

public sealed class BackupManifest
{
    public const string SchemaFamily = "nbtcli.next.backup-manifest";
    public const int SchemaVersion = 1;
    public Guid WorldId { get; }
    public ImmutableArray<BackupArtifact> Artifacts { get; }
    public BackupManifest(Guid worldId, IEnumerable<BackupArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var rows = artifacts.Take(100_001).ToArray();
        if (worldId == Guid.Empty || rows.Length is 0 or > 100_000 || rows.Any(row => row is null || row.Artifact is null || row.Image is null
                || row.Artifact.WorldId != worldId || row.Artifact.ContainerEntry is not null || row.Artifact.RelativePath.Length > 32_767)
            || rows.Select(row => row.Artifact.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length)
            throw new ArgumentException("Backup requires a unique bounded physical artifact set for one world", nameof(artifacts));
        WorldId = worldId; Artifacts = rows.OrderBy(row => row.Artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray();
    }
}
