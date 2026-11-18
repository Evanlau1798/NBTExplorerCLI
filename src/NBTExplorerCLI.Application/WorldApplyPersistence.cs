using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldApplyEngine
{
    private sealed record LoadedJournal(ApplyJournalHeader Header, ApplyJournal Journal, Hash256 Hash);
    private static byte[] Bytes(string value) => new UTF8Encoding(false, true).GetBytes(value);
    private static string Text(byte[] value) => new UTF8Encoding(false, true).GetString(value);
    private byte[] Object(Hash256 hash)
    {
        var bytes = objects.ReadBlob(hash) ?? throw new InvalidDataException("Journal object is missing");
        if (bytes.Length > 67_108_864 || Hash256.Compute(bytes) != hash) throw new InvalidDataException("Journal object content identity differs");
        return bytes;
    }
    private Hash256 SaveObject(byte[] bytes)
    {
        if (bytes.Length > 67_108_864) throw new IOException("Journal object exceeds byte budget");
        var hash = Hash256.Compute(bytes); objects.SaveBlob(hash, bytes); _ = Object(hash); return hash;
    }
    private LoadedJournal? Load(Hash256 planHash)
    {
        ArgumentNullException.ThrowIfNull(planHash); var bytes = journals.Read(planHash); if (bytes is null) return null;
        return DecodeJournal(planHash, bytes);
    }
    private LoadedJournal DecodeJournal(Hash256 planHash, byte[] bytes)
    {
        var hash = Hash256.Compute(bytes); if (!Object(hash).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Journal pointer differs from its immutable generation");
        var journal = ApplyJournalJson.Read(Text(bytes)); var header = ApplyJournalHeaderJson.Read(Text(Object(journal.HeaderHash)));
        if (journal.Binding.PlanHash != planHash || header.Plan.PlanHash != planHash || header.Plan.WorldId != inventory.WorldId
            || header.AccessBinding != Access() || journal.Binding != JournalBinding.From(header.Prepared) || journal.CompletedCount > header.Prepared.Artifacts.Length)
            throw new InvalidDataException("Journal header, plan, progress or world binding differs");
        return new(header, journal, hash);
    }
    private Hash256 JournalStoreBinding() => journals.AccessBinding ?? throw new NotSupportedException("Journal storage requires an exact access binding");
    private void CheckPending(IPendingWorldWrite pending, Hash256 requested)
    {
        if (pending.Current is not { } marker) return;
        if (marker.WorldBinding != Access()) throw new InvalidDataException("Pending write marker belongs to a different physical root");
        if (marker.PlanHash != requested || marker.StoreBinding != JournalStoreBinding()) throw new PendingWorldWriteException(marker.PlanHash);
    }
    private void BeginPending(IPendingWorldWrite pending, Hash256 plan, Hash256 anchor)
        => pending.Begin(new(plan, Access(), JournalStoreBinding(), anchor));
    private LoadedJournal LoadRecovery(Hash256 planHash, PendingWorldWrite? marker)
    {
        var active = Load(planHash);
        if (marker is null) return active ?? throw new ApplyJournalMissingException();
        var bytes = Object(marker.Anchor);
        var anchor = ApplyJournalJson.Read(Text(bytes));
        if (active is not null)
        {
            if (active.Journal.HeaderHash != anchor.HeaderHash || active.Journal.Sequence < anchor.Sequence)
                throw new InvalidDataException("Journal pointer differs from its pending write anchor");
            return active;
        }
        var initial = DecodeJournal(planHash, bytes);
        if (anchor.Phase != ApplyJournalPhase.Prepared || !PreimagesRestored(initial.Header))
            throw new InvalidDataException("Pending recovery is missing its journal pointer");
        journals.Write(planHash, bytes, null);
        return initial;
    }
    private bool PreimagesRestored(ApplyJournalHeader header)
    {
        var images = Images(header);
        return header.Plan.Preconditions.All(input => images[input.Artifact] == input.Image);
    }
    private static bool Terminal(ApplyReceipt receipt) => receipt.DependenciesComplete
        || receipt.Results.All(result => result.Outcome is OperationOutcome.Cancelled or OperationOutcome.Failed or OperationOutcome.Skipped or OperationOutcome.Unsupported);
    private static ApplyReceipt CloseIfTerminal(IPendingWorldWrite pending, ApplyReceipt receipt)
    {
        if (Terminal(receipt)) pending.Complete();
        return receipt;
    }
    private LoadedJournal Save(ApplyJournalHeader header, ApplyJournal journal, Hash256? expected)
    {
        var bytes = Bytes(ApplyJournalJson.Write(journal)); var hash = SaveObject(bytes);
        journals.Write(journal.Binding.PlanHash, bytes, expected); return new(header, journal, hash);
    }
    private LoadedJournal Advance(LoadedJournal previous, ApplyDirection direction, ApplyJournalPhase phase, int completed, Hash256? receipt)
        => Save(previous.Header, new(previous.Journal.Binding, previous.Journal.HeaderHash, checked(previous.Journal.Sequence + 1),
            previous.Hash, direction, phase, completed, receipt), previous.Hash);
    private ApplyReceipt? Receipt(LoadedJournal active)
        => active.Journal.ReceiptHash is { } hash ? ApplyReceiptJson.Read(Text(Object(hash)), active.Header.Plan, active.Header.Prepared) : null;
    private Dictionary<ArtifactAddress, ArtifactImage?> Images(ApplyJournalHeader header)
    {
        var images = new Dictionary<ArtifactAddress, ArtifactImage?>();
        foreach (var input in header.Plan.Preconditions)
        {
            try { images.Add(input.Artifact, inventory.ReadImage(input.Artifact)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { images.Add(input.Artifact, null); }
        }
        return images;
    }
    private static bool Known(ApplyJournalHeader header, IReadOnlyDictionary<ArtifactAddress, ArtifactImage?> images)
    {
        var post = header.Prepared.Artifacts.ToDictionary(output => output.Artifact, output => output.Postimage);
        return header.Plan.Preconditions.All(input => images[input.Artifact] is { } actual && (actual == input.Image
            || post.TryGetValue(input.Artifact, out var image) && actual == image));
    }
    private ApplyReceipt Finish(LoadedJournal active, IReadOnlyDictionary<ArtifactAddress, ArtifactImage?> images,
        ApplyDirection direction, string reason, CancellationToken cancellation, bool dependenciesVerified = true)
    {
        if (string.IsNullOrWhiteSpace(reason)) reason = "Apply did not complete; inspect the journal and current images";
        if (reason.Length > MaximumReceiptReasonCodeUnits) reason = reason[..(MaximumReceiptReasonCodeUnits - 1)] + "…";
        var header = active.Header; var outputs = header.Prepared.Artifacts.ToDictionary(output => output.Artifact, output => output.Postimage);
        var preimages = header.Plan.Preconditions.ToDictionary(input => input.Artifact, input => input.Image);
        var metadata = header.Plan.Preconditions.Where(input => !outputs.ContainsKey(input.Artifact)).ToArray();
        var metadataConflict = metadata.Any(input => images[input.Artifact] is { } image && image != input.Image);
        var metadataUnknown = metadata.Any(input => images[input.Artifact] is null);
        var unchangedWorld = header.Plan.Preconditions.All(input => images[input.Artifact] == input.Image);
        var results = header.Plan.Operations.Select(operation =>
        {
            var required = operation.RequiredOutputs.Distinct().ToArray();
            var conflict = metadataConflict || required.Any(artifact => images[artifact] is { } actual && actual != outputs[artifact] && actual != preimages[artifact]);
            if (conflict) return new OperationReceipt(operation.Id, OperationOutcome.Conflict, "Current content is a third image outside the journal authorization");
            var desired = required.All(artifact => images[artifact] is { } image && image == (direction == ApplyDirection.Forward ? outputs[artifact] : preimages[artifact]));
            if (desired && !metadataUnknown)
            {
                var same = required.All(artifact => outputs[artifact] == preimages[artifact]);
                var rollback = direction == ApplyDirection.Rollback && !same;
                var outcome = same ? OperationOutcome.Unchanged : rollback ? OperationOutcome.RolledBack : OperationOutcome.Applied;
                var proofs = required.Select(artifact => new ImageProof(rollback ? ImageProofKind.Preimage : ImageProofKind.Postimage,
                    artifact, rollback ? preimages[artifact] : outputs[artifact], images[artifact]!));
                return new OperationReceipt(operation.Id, outcome, rollback ? "Verified restoration of this plan's raw preimages" : null, proofs);
            }
            return new OperationReceipt(operation.Id, cancellation.IsCancellationRequested && unchangedWorld ? OperationOutcome.Cancelled : OperationOutcome.RecoveryRequired, reason);
        }).ToArray();
        var complete = dependenciesVerified && !metadataConflict && !metadataUnknown && header.Prepared.Artifacts.All(output => images[output.Artifact]
            == (direction == ApplyDirection.Forward ? output.Postimage : preimages[output.Artifact]));
        var prior = Receipt(active); var revision = prior is null ? 0 : checked(prior.Revision + 1);
        if (prior is not null) _ = prior.Revise(revision, results, complete);
        var receipt = header.Prepared.Receipt(header.Plan, revision, results, complete);
        var hash = SaveObject(Bytes(ApplyReceiptJson.Write(receipt)));
        _ = Advance(active, direction, Terminal(receipt) ? ApplyJournalPhase.Completed : ApplyJournalPhase.RecoveryRequired, active.Journal.CompletedCount, hash);
        return receipt;
    }
}
