using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldQueryService
{
    private sealed record ExistingCache(CacheInspection Inspection, ScanCacheManifest? Manifest, Hash256? HeadHash);
    private ExistingCache Existing(WorldQueryContext context, CancellationToken cancellationToken = default)
    {
        byte[]? bytes;
        try { bytes = store.ReadManifest(); }
        catch (IOException) { return new(new(CacheState.Corrupt, "ManifestReadFailed"), null, null); }
        if (bytes is null) return new(new(CacheState.Missing), null, null);
        var hash = Hash256.Compute(bytes); ScanCacheManifest manifest;
        try
        {
            manifest = ScanCacheJson.ReadManifest(new UTF8Encoding(false, true).GetString(bytes));
            if (manifest.WorldId != inventory.WorldId) return new(new(CacheState.Corrupt, "WorldIdentityChanged"), null, hash);
        }
        catch (NotSupportedException) { return new(new(CacheState.UnsupportedSchema, "CacheSchemaVersion"), null, hash); }
        catch (Exception error) when (CacheFailure(error)) { return new(new(CacheState.Corrupt, "InvalidCache"), null, hash); }
        try
        {
            foreach (var descriptor in manifest.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shard = CachedArtifactResults.Read(store, descriptor, inventory.WorldId);
                if (context.ParseKeys.TryGetValue(descriptor.RelativePath, out var key) && key == descriptor.ParseKey)
                    _ = CachedArtifactResults.Bind(shard, context.Groups[descriptor.RelativePath]);
            }
        }
        catch (NotSupportedException) { return new(new(CacheState.UnsupportedSchema, "ShardSchemaVersion"), manifest, hash); }
        catch (Exception error) when (CacheFailure(error)) { return new(new(CacheState.Corrupt, "InvalidShard"), manifest, hash); }
        CacheInspection result;
        if (!SameScope(manifest.Scope, context.Extraction.Discovery.Scope)) result = new(CacheState.Dirty, "FilterChanged");
        else if (!SameInterpretation(manifest.Versions, context.Versions)) result = new(CacheState.Dirty, "InterpretationChanged");
        else if (!manifest.InputSources.Select(source => source.Artifact).SequenceEqual(context.Inputs.Select(source => source.Artifact))) result = new(CacheState.Dirty, "SourceSetChanged");
        else if (!manifest.InputSources.SequenceEqual(context.Inputs)) result = new(CacheState.Dirty, "ContentChanged");
        else if (manifest.SnapshotId != context.SnapshotId || manifest.CatalogDiagnosticCode != context.Extraction.Discovery.DiagnosticCode) result = new(CacheState.Dirty, "MetadataChanged");
        else if (manifest.Artifacts.Length != context.Groups.Count || manifest.Artifacts.Any(artifact => !context.ParseKeys.TryGetValue(artifact.RelativePath, out var key) || key != artifact.ParseKey))
            return new(new(CacheState.Corrupt, "ArtifactBindingChanged"), null, hash);
        else if (context.Inputs.Any(source => source.ContentHash is null)) result = new(CacheState.Dirty, "UnverifiedSources");
        else result = new(CacheState.Fresh);
        return new(result, manifest, hash);
    }
    private static bool SameInterpretation(InterpretationVersions left, InterpretationVersions right)
        => left.Codec == right.Codec && left.Profile == right.Profile && left.Rules == right.Rules
            && left.Policy.Split('.').Take(2).SequenceEqual(right.Policy.Split('.').Take(2));
    private static bool SameScope(SnapshotScope left, SnapshotScope right)
    {
        static bool Same(IEnumerable<string> first, IEnumerable<string> second) => first.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SequenceEqual(second.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return Same(left.Dimensions, right.Dimensions) && Same(left.Regions, right.Regions) && Same(left.Types, right.Types) && Same(left.ArtifactPaths, right.ArtifactPaths);
    }
}
