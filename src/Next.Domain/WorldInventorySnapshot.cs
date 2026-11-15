using System.Collections.Immutable;

namespace Next.Domain;

public sealed record WorldInventoryEntry(string RelativePath, long Bytes, bool IsDirectory, SourceOutcome Outcome, string? DiagnosticCode = null);
public sealed class WorldInventorySnapshot
{
    public ImmutableArray<WorldInventoryEntry> Entries { get; }
    public ImmutableArray<string> DirectoryPaths { get; }
    public string? DiagnosticCode { get; }
    public bool Complete => DiagnosticCode is null && Entries.All(entry => entry.Outcome == SourceOutcome.Success);
    public WorldInventorySnapshot(IEnumerable<WorldInventoryEntry> entries, string? diagnosticCode = null, IEnumerable<string>? directoryPaths = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.Take(100_001).ToImmutableArray();
        DirectoryPaths = (directoryPaths ?? []).Take(100_001).ToImmutableArray();
        if (Entries.Length > 100_000 || Entries.Any(entry => entry is null || entry.RelativePath is null || entry.Bytes < 0 || !Enum.IsDefined(entry.Outcome)))
            throw new ArgumentException("Invalid or oversized inventory", nameof(entries));
        if (DirectoryPaths.Length > 100_000 || DirectoryPaths.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid or oversized directory inventory", nameof(directoryPaths));
        DiagnosticCode = diagnosticCode;
    }
}
