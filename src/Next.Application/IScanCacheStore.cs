using Next.Domain;

namespace Next.Application;

public interface IScanCacheStore
{
    byte[]? ReadManifest();
    byte[]? ReadShard(Hash256 hash);
    void WriteShard(Hash256 hash, ReadOnlyMemory<byte> bytes);
    void PublishManifest(ReadOnlyMemory<byte> bytes, Hash256? expectedManifest);
}
