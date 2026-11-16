namespace Next.Domain;

public enum ApplyJournalPhase { Prepared, Committing, ReadBack, Completed, RecoveryRequired }
public enum ApplyDirection { Forward, Rollback }

public sealed class ApplyJournal
{
    public const string SchemaFamily = "nbtcli.next.apply-journal";
    public const int SchemaVersion = 1;
    public JournalBinding Binding { get; }
    public Hash256 HeaderHash { get; }
    public long Sequence { get; }
    public Hash256? PreviousHash { get; }
    public ApplyDirection Direction { get; }
    public ApplyJournalPhase Phase { get; }
    public int CompletedCount { get; }
    public Hash256? ReceiptHash { get; }
    public ApplyJournal(JournalBinding binding, Hash256 headerHash, long sequence, Hash256? previousHash,
        ApplyDirection direction, ApplyJournalPhase phase, int completedCount, Hash256? receiptHash)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(binding.PlanHash);
        ArgumentNullException.ThrowIfNull(binding.ManifestHash); ArgumentNullException.ThrowIfNull(headerHash);
        if (sequence < 0 || (sequence == 0) != (previousHash is null) || (sequence == 0) != (phase == ApplyJournalPhase.Prepared)
            || !Enum.IsDefined(direction) || !Enum.IsDefined(phase) || completedCount is < 0 or > 100_000
            || phase == ApplyJournalPhase.Prepared && (direction != ApplyDirection.Forward || completedCount != 0 || receiptHash is not null)
            || phase == ApplyJournalPhase.Completed && receiptHash is null)
            throw new ArgumentException("Invalid journal generation, progress or completion binding");
        (Binding, HeaderHash, Sequence, PreviousHash, Direction, Phase, CompletedCount, ReceiptHash)
            = (binding, headerHash, sequence, previousHash, direction, phase, completedCount, receiptHash);
    }
}

public sealed class ApplyJournalHeader
{
    public const string SchemaFamily = "nbtcli.next.apply-journal-header";
    public const int SchemaVersion = 1;
    public PatchPlan Plan { get; }
    public PreparedManifest Prepared { get; }
    public Hash256 BackupManifestHash { get; }
    public Hash256 AccessBinding { get; }
    public ApplyJournalHeader(PatchPlan plan, PreparedManifest prepared, Hash256 backupManifestHash, Hash256 accessBinding)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(backupManifestHash);
        ArgumentNullException.ThrowIfNull(accessBinding);
        if (plan.PlanHash != prepared.PlanHash) throw new ArgumentException("Journal header must bind one exact plan and prepared manifest");
        (Plan, Prepared, BackupManifestHash, AccessBinding) = (plan, prepared, backupManifestHash, accessBinding);
    }
}
