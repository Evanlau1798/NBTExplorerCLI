using Next.Application;
using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Infrastructure;

public sealed class FileSystemWorldBackupStore : IWorldBackupStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemWorldBackupStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemWorldBackupStore(string root, ArtifactFileIo io) => repository = new(root, "nbtcli.next.backup-store", io);
    public byte[]? ReadBlob(Hash256 hash) => Read("blob-", hash, ".bin");
    public void SaveBlob(Hash256 hash, ReadOnlyMemory<byte> bytes) => Save("blob-", hash, ".bin", bytes);
    public byte[]? ReadManifest(Hash256 id) => Read("manifest-", id, ".json");
    public void SaveManifest(Hash256 id, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArgumentException("Backup manifest exceeds object budget", nameof(bytes));
        _ = BackupManifestJson.Read(new UTF8Encoding(false, true).GetString(bytes.Span)); Save("manifest-", id, ".json", bytes);
    }
    private byte[]? Read(string prefix, Hash256 hash, string extension)
    {
        ArgumentNullException.ThrowIfNull(hash); var bytes = repository.Read(prefix + hash.Hex + extension);
        if (bytes is not null && Hash256.Compute(bytes) != hash) throw new InvalidDataException("Immutable backup object failed its content hash"); return bytes;
    }
    private void Save(string prefix, Hash256 hash, string extension, ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArgumentException("Backup object exceeds storage budget", nameof(bytes));
        if (Hash256.Compute(bytes.Span) != hash) throw new ArgumentException("Backup object bytes differ from its content identity", nameof(bytes));
        void Same(byte[] prior) { if (!prior.AsSpan().SequenceEqual(bytes.Span)) throw new ArtifactConflictException("Immutable backup object already has different bytes"); }
        var existing = Read(prefix, hash, extension); if (existing is not null) { Same(existing); return; }
        try { repository.Write(prefix + hash.Hex + extension, bytes, null); }
        catch (ArtifactConflictException) { Same(Read(prefix, hash, extension) ?? throw new IOException("Backup object disappeared after conflict")); }
    }
}
