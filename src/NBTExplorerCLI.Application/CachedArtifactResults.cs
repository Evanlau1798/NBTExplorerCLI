using System.Collections.Immutable;
using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

internal static class CachedArtifactResults
{
    private sealed record Key(ArtifactAddress Artifact, WorldSourceRole Role, ChunkPosition? Chunk);
    internal static ArtifactCacheShard Read(IScanCacheStore store, CachedArtifact descriptor, Guid world)
    {
        var bytes = store.ReadShard(descriptor.ContentHash) ?? throw new InvalidDataException("Missing cache shard");
        if (Hash256.Compute(bytes) != descriptor.ContentHash) throw new InvalidDataException("Cache shard hash differs");
        var shard = ScanCacheJson.ReadShard(new UTF8Encoding(false, true).GetString(bytes));
        if (shard.ParseKey != descriptor.ParseKey || shard.Sources.Length != descriptor.SourceCount || shard.Sources.Any(source => source.Artifact.WorldId != world || source.Artifact.RelativePath != descriptor.RelativePath)
            || shard.Sources.Sum(source => source.Text?.Occurrences.Length ?? 0) != descriptor.TextCount || shard.Sources.Sum(source => source.Commands?.References.Length ?? 0) != descriptor.ReferenceCount
            || shard.Sources.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant) != descriptor.CoverageComplete) throw new InvalidDataException("Cache shard descriptor differs");
        return shard;
    }
    internal static ArtifactScanBatch Bind(ArtifactCacheShard shard, ImmutableArray<DiscoveredSource> sources)
    {
        var expected = sources.ToDictionary(source => new Key(source.Location.Artifact, source.Location.Role, source.Chunk));
        if (expected.Count != shard.Sources.Length) throw new InvalidDataException("Cache source catalog differs");
        var rows = shard.Sources.Select(source =>
        {
            if (!expected.TryGetValue(new(source.Artifact, source.Role, source.Chunk), out var discovered)) throw new InvalidDataException("Cache source binding differs");
            if (discovered.Outcome != SourceOutcome.Success && (source.Outcome != discovered.Outcome || source.DiagnosticCode != discovered.DiagnosticCode || source.Text is not null || source.Commands is not null))
                throw new InvalidDataException("Cache contradicts catalog outcome");
            if ((source.Text is null ? [] : Addresses(source.Text)).Concat(source.Commands is null ? [] : Addresses(source.Commands.Text)).Concat(source.Commands?.References.Select(reference => reference.Address) ?? [])
                .Any(address => address.Dimension != discovered.Location.Dimension)) throw new InvalidDataException("Cache dimension binding differs");
            return new TextSourceExtraction(discovered, source.Outcome, source.Text, source.DiagnosticCode, source.Commands);
        }).ToImmutableArray();
        return new(sources[0].Location.Artifact.RelativePath, rows);
    }
    internal static ArtifactCacheShard Create(ArtifactScanBatch batch, Hash256 parseKey)
        => new(parseKey, batch.Sources.Select(source => new CachedSource(source.Source.Location.Artifact, source.Source.Location.Role, source.Source.Chunk, source.Outcome, source.DiagnosticCode, source.Text, source.Commands)).ToImmutableArray());
    private static IEnumerable<SourceAddress> Addresses(TextExtractionResult text)
        => text.Occurrences.Select(item => item.Address).Concat(text.TranslationKeys.Select(item => item.Address)).Concat(text.Diagnostics.Select(item => item.Address)).Concat(text.PendingCommands.Select(item => item.Address)).Concat(text.Unclassified.Select(item => item.Address));
}
