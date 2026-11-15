using Next.Domain;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text;

namespace Next.Formats;

public static class ScanCacheJson
{
    private const string ShardFamily = "nbtcli.next.scan-shard";
    private const string ManifestFamily = "nbtcli.next.scan-manifest";
    private sealed record ScopeDto(string[] Dimensions, string[] Regions, string[] Types, string[] ArtifactPaths);
    private sealed record ManifestDto(Guid WorldId, ScopeDto Scope, InterpretationVersions Versions, StableIdentity SnapshotId,
        CachedArtifact[] Artifacts, ManifestSource[] InputSources, bool CoverageComplete, string? CatalogDiagnosticCode);
    public static Hash256 BindingHash(string? layoutProfile, IEnumerable<CacheSourceBinding> sources)
    {
        var bindings = sources.OrderBy(source => SourceIdentity.Artifact(source.Location.Artifact).Digest.Hex, StringComparer.Ordinal)
            .ThenBy(source => source.Location.Role).ThenBy(source => source.Chunk?.Z).ThenBy(source => source.Chunk?.X).ToArray();
        return Hash256.Compute(Encoding.UTF8.GetBytes(ContractJson.Write("nbtcli.next.scan-bindings", new { layoutProfile, sources = bindings })));
    }
    public static string WriteShard(ArtifactCacheShard shard)
    {
        ScanCacheValidation.Shard(shard); return ContractJson.Write(ShardFamily, shard);
    }
    public static ArtifactCacheShard ReadShard(string json) => ContractJson.Read(json, ShardFamily, data =>
    {
        ContractJson.Fields(data, "parseKey", "sources");
        var shard = ContractJson.Decode<ArtifactCacheShard>(data); ScanCacheValidation.Shard(shard);
        if (WriteShard(shard) != json) throw new JsonException("Cache shard is not canonical");
        return shard;
    });
    public static string WriteManifest(ScanCacheManifest manifest)
    {
        ScanCacheValidation.Manifest(manifest);
        return ContractJson.Write(ManifestFamily, new ManifestDto(manifest.WorldId, new(manifest.Scope.Dimensions.ToArray(), manifest.Scope.Regions.ToArray(), manifest.Scope.Types.ToArray(), manifest.Scope.ArtifactPaths.ToArray()),
            manifest.Versions, manifest.SnapshotId, manifest.Artifacts.ToArray(), manifest.InputSources.ToArray(), manifest.CoverageComplete, manifest.CatalogDiagnosticCode));
    }
    public static ScanCacheManifest ReadManifest(string json) => ContractJson.Read(json, ManifestFamily, data =>
    {
        ContractJson.Fields(data, "worldId", "scope", "versions", "snapshotId", "artifacts", "inputSources", "coverageComplete", "catalogDiagnosticCode");
        var dto = ContractJson.Decode<ManifestDto>(data); var scope = ContractJson.Required(dto.Scope);
        if (scope.Dimensions is null || scope.Regions is null || scope.Types is null || scope.ArtifactPaths is null || dto.Artifacts is null || dto.InputSources is null) throw new JsonException("Missing cache manifest collections");
        var result = new ScanCacheManifest(dto.WorldId, new(scope.Dimensions, scope.Regions, scope.Types, scope.ArtifactPaths), dto.Versions, dto.SnapshotId,
            dto.Artifacts.ToImmutableArray(), dto.InputSources.ToImmutableArray(), dto.CoverageComplete, dto.CatalogDiagnosticCode);
        ScanCacheValidation.Manifest(result);
        if (WriteManifest(result) != json) throw new JsonException("Cache manifest is not canonical");
        return result;
    });
}
