using Next.Application;
using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Infrastructure;

public sealed class FileSystemLegacyImportStore : ILegacyImportStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemLegacyImportStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemLegacyImportStore(string root, ArtifactFileIo io) => repository = new(root, "nbtcli.next.legacy-import-store", io);
    public byte[]? Read(Hash256 importId)
    {
        var bytes = repository.Read(Key(importId));
        if (bytes is not null && LegacyImportJson.Read(new UTF8Encoding(false, true).GetString(bytes)).ImportId != importId) throw new InvalidDataException("Legacy report identity differs");
        return bytes;
    }
    public void Save(Hash256 importId, ReadOnlyMemory<byte> bundle)
    {
        if (LegacyImportJson.Read(new UTF8Encoding(false, true).GetString(bundle.Span)).ImportId != importId) throw new ArgumentException("Legacy report identity differs", nameof(bundle));
        var previous = Read(importId);
        if (previous is not null) { Same(previous, bundle); return; }
        try { repository.Write(Key(importId), bundle, null); }
        catch (ArtifactConflictException) { Same(Read(importId) ?? throw new IOException("Legacy report disappeared after conflict"), bundle); }
    }
    private static void Same(byte[] previous, ReadOnlyMemory<byte> requested)
    { if (!previous.AsSpan().SequenceEqual(requested.Span)) throw new ArtifactConflictException("An immutable legacy report already has different bytes"); }
    private static string Key(Hash256 id) { ArgumentNullException.ThrowIfNull(id); return "import-" + id.Hex + ".json"; }
}
