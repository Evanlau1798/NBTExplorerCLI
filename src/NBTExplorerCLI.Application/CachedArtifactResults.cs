using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

internal static class CachedArtifactResults
{
    private sealed record Key(ArtifactAddress Artifact, WorldSourceRole Role, ChunkPosition? Chunk);
    internal static ArtifactCacheShard Read(IScanCacheStore store, CachedArtifact descriptor, Guid world)
    {
        byte[] ReadPart(Hash256 hash)
        {
            var bytes = store.ReadShard(hash) ?? throw new InvalidDataException("Missing cache shard");
            if (Hash256.Compute(bytes) != hash) throw new InvalidDataException("Cache shard hash differs");
            return bytes;
        }
        var shard = ScanCacheJson.ReadStoredShard(ReadPart(descriptor.ContentHash), descriptor.SourceCount, ReadPart);
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
    internal static CachedArtifact Write(IScanCacheStore store, ArtifactScanBatch batch, Hash256 parseKey, CancellationToken token)
    {
        var sources = batch.Sources.Select(source => new CachedSource(source.Source.Location.Artifact, source.Source.Location.Role, source.Source.Chunk,
            source.Outcome, source.DiagnosticCode, source.Text, source.Commands)).ToImmutableArray();
        Hash256 Put(byte[] bytes)
        {
            token.ThrowIfCancellationRequested();
            var hash = Hash256.Compute(bytes);
            store.WriteShard(hash, bytes);
            return hash;
        }
        CachedArtifact Describe(Hash256 hash, ImmutableArray<CachedSource> rows)
            => new(batch.RelativePath, parseKey, hash, rows.Length, rows.Sum(row => row.Text?.Occurrences.Length ?? 0),
                rows.Sum(row => row.Commands?.References.Length ?? 0), rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant));

        CachedSource Failure(CachedSource row, int index)
        {
            var discovery = batch.Sources[index].Source;
            return row with { Outcome = discovery.Outcome == SourceOutcome.Success ? SourceOutcome.Failed : discovery.Outcome,
                DiagnosticCode = discovery.Outcome == SourceOutcome.Success ? "CacheSerializationLimit" : discovery.DiagnosticCode, Text = null, Commands = null };
        }
        CachedArtifact? Store(ImmutableArray<CachedSource> input)
        {
            var whole = ScanCacheJson.EncodeShardWithinBudget(new(parseKey, input), out var retained);
            if (whole is not null) return Describe(Put(whole), input);
            if (retained > DecodeLimits.Default.MaxDecodedBytes) return null;

            var parts = ImmutableArray.CreateBuilder<Hash256>();
            var stored = ImmutableArray.CreateBuilder<CachedSource>();
            var remaining = ScanCacheJson.MaximumStoredShardBytes;
            bool Publish(int offset, int count, bool overBudget = false)
            {
                token.ThrowIfCancellationRequested();
                var rows = input.Slice(offset, count);
                var bytes = overBudget ? null : ScanCacheJson.EncodeShardWithinBudget(new(parseKey, rows), out _);
                if (bytes is null && count > 1)
                {
                    var midpoint = count / 2;
                    return Publish(offset, midpoint) && Publish(offset + midpoint, count - midpoint);
                }
                if (bytes is null)
                {
                    rows = [Failure(rows[0], offset)];
                    bytes = ScanCacheJson.EncodeShardWithinBudget(new(parseKey, rows), out _);
                }
                if (bytes is null || bytes.Length > remaining) return false;
                remaining -= bytes.Length;
                parts.Add(Put(bytes));
                stored.AddRange(rows);
                return true;
            }
            if (!Publish(0, input.Length, overBudget: true)) return null;
            if (parts.Count == 1) return Describe(parts[0], stored.ToImmutable());
            var index = ScanCacheJson.WriteShardIndex(parseKey, parts.ToImmutable());
            return index.Length <= remaining ? Describe(Put(index), stored.ToImmutable()) : null;
        }
        return Store(sources) ?? Store(sources.Select(Failure).ToImmutableArray())
            ?? throw new CodecException(CodecError.LimitExceeded, 0, "Cache diagnostics exceed their serialization budget");
    }
    private static IEnumerable<SourceAddress> Addresses(TextExtractionResult text)
        => text.Occurrences.Select(item => item.Address).Concat(text.TranslationKeys.Select(item => item.Address)).Concat(text.Diagnostics.Select(item => item.Address)).Concat(text.PendingCommands.Select(item => item.Address)).Concat(text.Unclassified.Select(item => item.Address));
}
