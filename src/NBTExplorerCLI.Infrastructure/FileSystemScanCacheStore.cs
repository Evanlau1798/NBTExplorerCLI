using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Infrastructure;

public sealed class FileSystemScanCacheStore : IScanCacheStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemScanCacheStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemScanCacheStore(string root, ArtifactFileIo io) => repository = new(root, "nbtcli.scan-cache-store", io);
    public byte[]? ReadManifest() => repository.Read("manifest.json");
    public byte[]? ReadShard(Hash256 hash)
    {
        ArgumentNullException.ThrowIfNull(hash); var bytes = repository.Read(Key(hash));
        if (bytes is not null && Hash256.Compute(bytes) != hash) throw new InvalidDataException("Cache shard hash differs");
        return bytes;
    }
    public void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (Hash256.Compute(bytes.Span) != hash) throw new ArgumentException("Shard content does not match its identity", nameof(bytes));
        var current = repository.Read(Key(hash)); var before = current is null ? null : Hash256.Compute(current);
        if (before == hash)
        {
            if (!current!.AsSpan().SequenceEqual(bytes.Span)) throw new InvalidDataException("Cache shard hash collision");
            return;
        }
        repository.Write(Key(hash), bytes, before);
    }
    public void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expectedManifest) => repository.Write("manifest.json", bytes, expectedManifest);
    private static string Key(Hash256 hash) => hash.Hex + ".shard.json";
}
