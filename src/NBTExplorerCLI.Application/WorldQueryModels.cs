using System.Collections.Immutable;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

public enum WorldQueryPurpose { Scan, Extract, Session, Convert }
public sealed record WorldQueryRequest(ScanRequest Scan, WorldQueryPurpose Purpose = WorldQueryPurpose.Scan, ScanExecutionOptions? Execution = null, bool ForceFull = false,
    IReadOnlyDictionary<ArtifactAddress, IReadOnlyList<StorageTextRule>>? StorageRules = null);
public sealed record WorldQueryTelemetry(int ManifestHits, int ManifestMisses, int Rebuilds, int ScannedArtifacts, int ReusedArtifacts,
    int ScannedSources, int ReusedSources, long ElapsedMilliseconds, bool Published);
public sealed record WorldQueryResult(WorldQuerySnapshot? Snapshot, CacheInspection Before, ScanCompletion? Scan, WorldQueryTelemetry Telemetry);
public sealed class WorldQuerySnapshot
{
    public ScanCacheManifest Manifest { get; }
    public ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> StorageRules { get; }
    internal WorldDiscoveryResult Discovery { get; }
    internal DecodeLimits Limits { get; }
    private readonly Func<IEnumerable<TextSourceExtraction>> read;
    internal WorldQuerySnapshot(ScanCacheManifest manifest, Func<IEnumerable<TextSourceExtraction>> read, ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> storageRules, WorldDiscoveryResult discovery, DecodeLimits limits)
        => (Manifest, this.read, StorageRules, Discovery, Limits) = (manifest, read, storageRules, discovery, limits);
    public IEnumerable<TextSourceExtraction> ReadSources() => read();
}
