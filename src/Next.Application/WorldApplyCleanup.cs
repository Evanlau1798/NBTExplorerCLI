using System.Text.Json;
using Next.Domain;

namespace Next.Application;

public sealed partial class WorldApplyEngine
{
    private const int CleanupByteLimit = 16 * 1024 * 1024;
    private const int CleanupEntryLimit = 100_000;
    private const string CleanupSchema = "nbtcli.next.candidate-cleanup-v1";
    private sealed record StoredProof(string Path, string FileIdentity, string ImageHash);
    private sealed record CleanupLedger(string Schema, string PlanHash, string AccessBinding, StoredProof[] Entries);

    private void RecordCandidates(PatchPlan plan, Hash256 access, IReadOnlyList<IArtifactCandidate> candidates)
    {
        var current = candidates.Select(candidate => candidate.CleanupProof).OfType<CandidateCleanupProof>().ToArray();
        if (current.Length == 0) return;
        try
        {
            var (previous, entries) = CandidateProofs(plan.PlanHash, access);
            var combined = entries.Concat(current).Distinct().ToArray();
            if (combined.Length != entries.Length) WriteCandidateProofs(plan.PlanHash, access, previous, combined);
        }
        catch (Exception error) when (CleanupFailure(error)) { }
    }

    private void CollectInactive(PatchPlan plan, Hash256 access, ApplyReceipt receipt)
    {
        if (plan.Outputs.IsEmpty || !receipt.DependenciesComplete
            || receipt.Results.Any(row => row.Outcome is not (OperationOutcome.Applied or OperationOutcome.Unchanged or OperationOutcome.RolledBack))) return;
        try
        {
            var (previous, entries) = CandidateProofs(plan.PlanHash, access);
            if (entries.Length == 0) return;
            var remaining = storage.CollectInactive(access, entries);
            if (remaining.Count < entries.Length) WriteCandidateProofs(plan.PlanHash, access, previous, remaining);
        }
        catch (Exception error) when (CleanupFailure(error)) { }
    }

    private (byte[]? Previous, CandidateCleanupProof[] Entries) CandidateProofs(Hash256 plan, Hash256 access)
    {
        var raw = journals.ReadCleanup(plan);
        if (raw is null) return (null, []);
        if (raw.Length > CleanupByteLimit) throw new InvalidDataException("Candidate cleanup ledger exceeds its budget");
        var ledger = JsonSerializer.Deserialize<CleanupLedger>(raw);
        if (ledger?.Schema != CleanupSchema || ledger.PlanHash != plan.Hex || ledger.AccessBinding != access.Hex
            || ledger.Entries is null || ledger.Entries.Length > CleanupEntryLimit
            || !raw.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(ledger)))
            throw new InvalidDataException("Invalid candidate cleanup ledger");
        return (raw, ledger.Entries.Select(entry => entry is { Path.Length: > 0, FileIdentity: not null, ImageHash: not null }
            ? new CandidateCleanupProof(entry.Path, new(entry.FileIdentity), new(entry.ImageHash))
            : throw new InvalidDataException("Invalid candidate cleanup proof")).ToArray());
    }

    private void WriteCandidateProofs(Hash256 plan, Hash256 access, byte[]? previous, IReadOnlyList<CandidateCleanupProof> entries)
    {
        if (entries.Count > CleanupEntryLimit || entries.Sum(entry => entry.Path.Length * 6L + 256) > CleanupByteLimit - 1024) return;
        var stored = entries.Select(entry => new StoredProof(entry.Path, entry.FileIdentity.Hex, entry.ImageHash.Hex)).ToArray();
        var raw = JsonSerializer.SerializeToUtf8Bytes(new CleanupLedger(CleanupSchema, plan.Hex, access.Hex, stored));
        journals.WriteCleanup(plan, raw, previous is null ? null : Hash256.Compute(previous));
    }

    private static bool CleanupFailure(Exception error)
        => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException;
}
