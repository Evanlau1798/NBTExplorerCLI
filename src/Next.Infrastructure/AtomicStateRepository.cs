using System.Text.Json;
using Next.Domain;

namespace Next.Infrastructure;

internal sealed class AtomicStateRepository
{
    private static readonly Guid StoreNamespace = Guid.Parse("5cc55a3c-a755-4b55-8dd6-045ea54439fa");
    private const string OwnerName = "owner.json";
    private readonly string root;
    private readonly string family;
    private readonly ArtifactStorage storage;
    private readonly object writeGate = new();
    internal AtomicStateRepository(string root, string family, ArtifactFileIo io)
    {
        this.root = SafeStateDirectory.Ensure(root); this.family = family;
        storage = new(this.root, StoreNamespace, io);
        using var directory = GuardedArtifactPath.AcquireDirectory(this.root, "");
        var owner = Owner();
        if (owner is null)
        {
            if (Directory.EnumerateFileSystemEntries(this.root).Any(path => Path.GetFileName(path) != ".nbtcli-writer.lock"))
                throw new IOException("Refusing a nonempty unowned state directory");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaFamily = family, schemaVersion = 1 });
            WriteCore(OwnerName, bytes, null);
        }
        else ValidateOwner(owner);
    }
    internal byte[]? Read(string key)
    {
        ValidateOwner(Owner() ?? throw new IOException("Missing state ownership"));
        try { return storage.Read(Address(key)); } catch (FileNotFoundException) { return null; }
    }
    internal void Write(string key, ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        ValidateOwner(Owner() ?? throw new IOException("Missing state ownership")); WriteCore(key, bytes, expected);
    }
    private static ArtifactAddress Address(string key)
    {
        if (key.Contains('/') || key.Contains('\\')) throw new ArgumentException("State keys must be plain filenames", nameof(key));
        return new(StoreNamespace, key, ArtifactKind.Json);
    }
    private void WriteCore(string key, ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        // A shared repository must keep the preimage check and replacement in one critical section.
        lock (writeGate)
        {
            using var candidate = storage.Stage(Address(key), expected is null ? ArtifactImage.Absent : ArtifactImage.Present(expected), bytes);
            candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
        }
    }
    private byte[]? Owner()
    {
        using var directory = GuardedArtifactPath.AcquireDirectory(root, "");
        try
        {
            using var stream = GuardedArtifactPath.OpenFile(Path.Combine(root, OwnerName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 4096) throw new IOException("Oversized state ownership record");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
        }
        catch (FileNotFoundException) { return null; }
    }
    private void ValidateOwner(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes); var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2 || value.GetProperty("schemaFamily").GetString() != family || value.GetProperty("schemaVersion").GetInt32() != 1)
                throw new IOException("Foreign or unsupported state directory");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new IOException("Invalid state ownership record", error); }
    }
}
