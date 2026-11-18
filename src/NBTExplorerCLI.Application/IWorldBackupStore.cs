using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

public interface IWorldBackupStore
{
    byte[]? ReadBlob(Hash256 hash);
    void SaveBlob(Hash256 hash, ReadOnlyMemory<byte> bytes);
    byte[]? ReadManifest(Hash256 id);
    void SaveManifest(Hash256 id, ReadOnlyMemory<byte> bytes);
}
