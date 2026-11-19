using NBTExplorerCLI.Domain;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text;

namespace NBTExplorerCLI.Formats;

public static class ScanCacheJson
{
    public const int MaximumStoredShardBytes = ContractJson.MaximumBytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string ShardFamily = "nbtcli.scan-shard";
    private const string ShardIndexFamily = "nbtcli.scan-shard-index";
    private const string ManifestFamily = "nbtcli.scan-manifest";
    private sealed record ScopeDto(string[] Dimensions, string[] Regions, string[] Types, string[] ArtifactPaths);
    private sealed record ShardIndex(Hash256 ParseKey, ImmutableArray<Hash256> Parts);
    private sealed record ManifestDto(Guid WorldId, ScopeDto Scope, InterpretationVersions Versions, StableIdentity SnapshotId,
        CachedArtifact[] Artifacts, ManifestSource[] InputSources, bool CoverageComplete, string? CatalogDiagnosticCode);
    public static Hash256 BindingHash(string? layoutProfile, IEnumerable<CacheSourceBinding> sources)
    {
        var bindings = sources.OrderBy(source => SourceIdentity.Artifact(source.Location.Artifact).Digest.Hex, StringComparer.Ordinal)
            .ThenBy(source => source.Location.Role).ThenBy(source => source.Chunk?.Z).ThenBy(source => source.Chunk?.X).ToArray();
        return Hash256.Compute(Encoding.UTF8.GetBytes(ContractJson.Write("nbtcli.scan-bindings", new { layoutProfile, sources = bindings })));
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
    public static byte[]? EncodeShardWithinBudget(ArtifactCacheShard shard, out long retained)
    {
        retained = ScanCacheValidation.Shard(shard, checkRetention: false);
        if (retained > DecodeLimits.Default.MaxDecodedBytes) return null;
        try { return Encoding.UTF8.GetBytes(ContractJson.WriteForStorage(ShardFamily, shard)); }
        catch (JsonOutputLimitException) { return null; }
    }
    public static byte[] WriteShardIndex(Hash256 parseKey, ImmutableArray<Hash256> parts)
        => ContractJson.WriteUtf8(ShardIndexFamily, ValidateIndex(new(parseKey, parts), 100_000));

    public static ArtifactCacheShard ReadStoredShard(byte[] bytes, int sourceCount, Func<Hash256, byte[]> readPart)
    {
        if (sourceCount is < 1 or > 100_000 || bytes.Length > MaximumStoredShardBytes) throw new JsonException("Invalid cache shard size");
        var json = StrictUtf8.GetString(bytes);
        if (!json.StartsWith("{\"schemaFamily\":\"" + ShardIndexFamily + "\",", StringComparison.Ordinal)) return ReadShard(json);
        var index = ContractJson.Read(json, ShardIndexFamily, data =>
        {
            ContractJson.Fields(data, "parseKey", "parts");
            var decoded = ValidateIndex(ContractJson.Decode<ShardIndex>(data), sourceCount);
            if (ContractJson.Write(ShardIndexFamily, decoded) != json) throw new JsonException("Cache shard index is not canonical");
            return decoded;
        });
        var sources = ImmutableArray.CreateBuilder<CachedSource>(); long retained = 0;
        var remaining = MaximumStoredShardBytes - bytes.Length;
        foreach (var hash in index.Parts)
        {
            var encoded = readPart(hash);
            if (encoded.Length > remaining) throw new JsonException("Cache shard parts exceed the aggregate byte budget");
            remaining -= encoded.Length;
            var part = ReadShard(StrictUtf8.GetString(encoded));
            retained = checked(retained + ScanCacheValidation.Shard(part));
            if (part.ParseKey != index.ParseKey || part.Sources.Length > sourceCount - sources.Count || retained > DecodeLimits.Default.MaxDecodedBytes)
                throw new JsonException("Cache shard part exceeds its aggregate binding or budget");
            sources.AddRange(part.Sources);
        }
        var result = new ArtifactCacheShard(index.ParseKey, sources.ToImmutable());
        ScanCacheValidation.Shard(result);
        return result;
    }
    private static ShardIndex ValidateIndex(ShardIndex index, int maximumParts)
    {
        if (index is null || index.ParseKey is null || index.Parts.IsDefault || index.Parts.Length < 2 || index.Parts.Length > maximumParts
            || index.Parts.Any(part => part is null) || index.Parts.Distinct().Count() != index.Parts.Length) throw new JsonException("Invalid cache shard index");
        return index;
    }
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
