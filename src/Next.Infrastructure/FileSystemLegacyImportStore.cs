using Next.Application;
using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Infrastructure;

public sealed class FileSystemLegacyImportStore : ILegacyImportStore
{
    private readonly AtomicStateRepository repository;
    private readonly string root;
    public FileSystemLegacyImportStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemLegacyImportStore(string root, ArtifactFileIo io)
    {
        this.root = Path.GetFullPath(root); repository = new(this.root, "nbtcli.next.legacy-import-store", io);
    }
    public IReadOnlyList<Hash256> ListIds()
    {
        using var directory = GuardedArtifactPath.AcquireDirectory(root, "");
        var paths = Directory.EnumerateFileSystemEntries(root).Take(100_001).ToArray();
        if (paths.Length > 100_000) throw new LegacyImportLimitException("Legacy report directory exceeds enumeration budget");
        try
        {
            return paths.Select(Path.GetFileName).Where(name => name!.StartsWith("import-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
                .Select(name => new Hash256(name![7..^5])).OrderBy(id => id.Hex, StringComparer.Ordinal).ToArray();
        }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid legacy report filename", error); }
    }
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
