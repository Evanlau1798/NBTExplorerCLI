using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;

namespace NBTExplorerCLI.Infrastructure;

public sealed class FileSystemApplyJournalStore : IApplyJournalStore
{
    public Hash256 AccessBinding => repository.AccessBinding;
    private readonly AtomicStateRepository repository;
    public FileSystemApplyJournalStore(string root) : this(root, new ArtifactFileIo()) { }
    internal FileSystemApplyJournalStore(string root, ArtifactFileIo io) => repository = new(root, "nbtcli.apply-journal-store", io);
    public byte[]? Read(Hash256 planHash) => repository.Read(Key(planHash));
    byte[]? IApplyJournalStore.ReadCleanup(Hash256 planHash) => repository.Read("cleanup-" + Key(planHash));
    void IApplyJournalStore.WriteCleanup(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        => repository.Write("cleanup-" + Key(planHash), bytes, expected);
    public void Write(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        var key = Key(planHash);
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArgumentException("Journal exceeds object budget", nameof(bytes));
        var journal = ApplyJournalJson.Read(new UTF8Encoding(false, true).GetString(bytes.Span));
        if (journal.Binding.PlanHash != planHash || journal.PreviousHash != expected) throw new ArgumentException("Journal differs from the requested plan or previous generation", nameof(bytes));
        repository.Write(key, bytes, expected);
    }
    private static string Key(Hash256 planHash)
    {
        ArgumentNullException.ThrowIfNull(planHash); return "journal-" + planHash.Hex + ".json";
    }
}
