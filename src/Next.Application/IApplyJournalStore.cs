using Next.Domain;

namespace Next.Application;

public interface IApplyJournalStore
{
    byte[]? Read(Hash256 planHash);
    void Write(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected);
}
