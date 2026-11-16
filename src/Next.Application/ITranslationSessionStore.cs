using Next.Domain;

namespace Next.Application;

public interface ITranslationSessionStore
{
    byte[]? Read(Guid sessionId);
    void Write(Guid sessionId, ReadOnlyMemory<byte> bytes, Hash256? expected);
}
