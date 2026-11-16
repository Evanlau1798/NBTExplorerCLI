using Next.Domain;

namespace Next.Application;

public interface ILegacyImportStore
{
    byte[]? Read(Hash256 importId);
    void Save(Hash256 importId, ReadOnlyMemory<byte> bundle);
}
