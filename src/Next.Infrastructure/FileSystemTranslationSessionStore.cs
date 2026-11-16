using Next.Application;
using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Infrastructure;

public sealed class FileSystemTranslationSessionStore : ITranslationSessionStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemTranslationSessionStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemTranslationSessionStore(string root, ArtifactFileIo io) => repository = new(root, "nbtcli.next.translation-session-store", io);
    public byte[]? Read(Guid sessionId) => repository.Read(Key(sessionId));
    public void Write(Guid sessionId, ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        var key = Key(sessionId); var state = TranslationSessionJson.Read(new UTF8Encoding(false, true).GetString(bytes.Span));
        if (state.SessionId != sessionId) throw new ArgumentException("Session record does not match the requested identity", nameof(bytes));
        repository.Write(key, bytes, expected);
    }
    private static string Key(Guid id) => id == Guid.Empty ? throw new ArgumentException("Session identity is required", nameof(id)) : "session-" + id.ToString("N") + ".json";
}
