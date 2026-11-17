using System.Collections.Concurrent;
using Next.Application;
using Next.Domain;

namespace Next.Benchmarks;

internal sealed class MeasuredInventory(IWorldInventory inner) : IWorldInventory
{
    private readonly ConcurrentDictionary<string, long> sizes = new(StringComparer.OrdinalIgnoreCase);
    internal long Reads, ReadBytes;
    internal long UniqueBytes => sizes.Values.Sum();
    public Guid WorldId => inner.WorldId;
    public Hash256? AccessBinding => inner.AccessBinding;
    public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => inner.Enumerate(maxEntries);
    public byte[] Read(ArtifactAddress artifact)
    {
        var bytes = inner.Read(artifact);
        Interlocked.Increment(ref Reads); Interlocked.Add(ref ReadBytes, bytes.Length);
        sizes.TryAdd(artifact.RelativePath, bytes.Length); return bytes;
    }
}

internal sealed class MeasuredCache(IScanCacheStore inner) : IScanCacheStore
{
    internal long ReadBytes, SubmittedBytes;
    public byte[]? ReadManifest()
    {
        var bytes = inner.ReadManifest(); Interlocked.Add(ref ReadBytes, bytes?.Length ?? 0); return bytes;
    }
    public byte[]? ReadShard(Hash256 hash)
    {
        var bytes = inner.ReadShard(hash); Interlocked.Add(ref ReadBytes, bytes?.Length ?? 0); return bytes;
    }
    public void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes)
    {
        inner.WriteShard(hash, bytes); Interlocked.Add(ref SubmittedBytes, bytes.Length);
    }
    public void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expectedManifest)
    {
        inner.PublishManifest(bytes, expectedManifest); Interlocked.Add(ref SubmittedBytes, bytes.Length);
    }
}
