using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

public interface IScanCacheStore
{
    byte[]? ReadManifest();
    byte[]? ReadShard(Hash256 hash);
    void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes);
    void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expectedManifest);
}
