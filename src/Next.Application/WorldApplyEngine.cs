using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Application;

public sealed partial class WorldApplyEngine
{
    internal const int MaximumReceiptReasonCodeUnits = 256;
    private readonly IWorldInventory inventory;
    private readonly IArtifactStorage storage;
    private readonly IWorldBackupStore objects;
    private readonly IApplyJournalStore journals;
    public WorldApplyEngine(IWorldInventory inventory, IArtifactStorage storage, IWorldBackupStore objects, IApplyJournalStore journals)
    {
        (this.inventory, this.storage, this.objects, this.journals) = (inventory ?? throw new ArgumentNullException(nameof(inventory)),
            storage ?? throw new ArgumentNullException(nameof(storage)), objects ?? throw new ArgumentNullException(nameof(objects)), journals ?? throw new ArgumentNullException(nameof(journals)));
        _ = Access();
    }
    private Hash256 Access()
    {
        var binding = inventory.AccessBinding;
        return binding is not null && storage.AccessBinding == binding ? binding : throw new ArgumentException("Apply inventory and storage must bind the same world root");
    }
    public ApplyReceipt Apply(PreparedWorldPlan prepared, bool worldOffline, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(prepared); Offline(worldOffline);
        LoadedJournal? active = null;
        try
        {
            if (prepared.AccessBinding != Access()) throw new ArgumentException("Prepared candidates belong to a different world root", nameof(prepared));
            if (Load(prepared.Plan.PlanHash) is not null) { prepared.Dispose(); return Recover(prepared.Plan.PlanHash, true, cancellation: cancellation); }
            WorldPlanBuilder.RequireReferencePolicy(prepared.Plan);
            if (prepared.Plan.WorldId != inventory.WorldId) throw new ArtifactConflictException("Prepared plan belongs to another world");
            if (prepared.Plan.Preconditions.IsEmpty) return prepared.Manifest.Receipt(prepared.Plan, 0, [], true);
            if (prepared.Candidates.Count != prepared.Manifest.Artifacts.Length) throw new ArgumentException("Incomplete prepared candidate set", nameof(prepared));
            for (var index = 0; index < prepared.Candidates.Count; index++)
            {
                var candidate = prepared.Candidates[index]; var expected = prepared.Manifest.Artifacts[index].Postimage;
                if (candidate.Postimage != expected) throw new ArtifactConflictException("Prepared candidate postimage differs from its manifest");
                candidate.Verify(stream =>
                {
                    if (expected.ExpectedAbsent) { if (stream.Length != 0) throw new ArtifactConflictException("Invalid deletion marker"); return; }
                    if (stream.Length > 67_108_864) throw new IOException("Postimage exceeds object budget");
                    var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                    if (SaveObject(bytes) != expected.Hash) throw new ArtifactConflictException("Prepared postimage differs from persisted candidate bytes");
                });
            }
            var backup = new WorldBackupService(inventory, objects).Create(prepared.Plan.Preconditions);
            WorldPlanBuilder.CheckPreimages(inventory, prepared.Plan);
            if (prepared.AccessBinding != Access()) throw new ArtifactConflictException("World root access changed before journal publication");
            var header = new ApplyJournalHeader(prepared.Plan, prepared.Manifest, backup.ManifestHash, prepared.AccessBinding);
            var headerHash = SaveObject(Bytes(ApplyJournalHeaderJson.Write(header)));
            active = Save(header, new(JournalBinding.From(prepared.Manifest), headerHash, 0, null, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null), null);
            return Commit(active, prepared.Candidates, ApplyDirection.Forward, cancellation);
        }
        catch (Exception error) when (active is not null && error is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        { return Interrupted(active!, ApplyDirection.Forward, error, cancellation); }
        finally { prepared.Dispose(); }
    }
    public ApplyReceipt Recover(Hash256 planHash, bool worldOffline, bool rollback = false, CancellationToken cancellation = default)
    {
        Offline(worldOffline); var active = Load(planHash) ?? throw new FileNotFoundException("No journal for the requested plan");
        ApplyReceiptJson.ValidateStorageBudget(active.Header.Plan, MaximumReceiptReasonCodeUnits);
        var previous = Receipt(active);
        var direction = rollback || active.Journal.Direction == ApplyDirection.Rollback ? ApplyDirection.Rollback : ApplyDirection.Forward;
        if (previous is not null && (active.Journal.Phase == ApplyJournalPhase.Completed && direction == active.Journal.Direction
            || previous.Results.Any(result => result.Outcome is OperationOutcome.Conflict or OperationOutcome.Cancelled or OperationOutcome.Failed or OperationOutcome.Unsupported or OperationOutcome.Skipped))) return previous;
        if (direction == ApplyDirection.Forward) WorldPlanBuilder.RequireReferencePolicy(active.Header.Plan);
        var candidates = new List<IArtifactCandidate>();
        try
        {
            var backup = new WorldBackupService(inventory, objects).Load(active.Header.BackupManifestHash);
            var preimages = active.Header.Plan.Preconditions.ToDictionary(input => input.Artifact, input => input.Image);
            var saved = backup.Manifest.Artifacts.ToDictionary(input => input.Artifact, input => input.Image);
            if (saved.Count != preimages.Count || preimages.Any(input => !saved.TryGetValue(input.Key, out var image) || image != input.Value))
                throw new InvalidDataException("Journal backup does not exactly bind its plan preimages");
            var images = Images(active.Header);
            if (!Known(active.Header, images)) return Finish(active, images, direction, "Source image is unknown or conflicts with the journal", cancellation);
            foreach (var output in active.Header.Prepared.Artifacts)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = direction == ApplyDirection.Forward ? output.Postimage : preimages[output.Artifact];
                var current = images[output.Artifact]!;
                var candidate = target.ExpectedAbsent ? storage.StageDelete(output.Artifact, current) : storage.Stage(output.Artifact, current, Object(target.Hash!));
                candidates.Add(candidate); candidate.Flush();
                candidate.Verify(stream =>
                {
                    if (target.ExpectedAbsent ? stream.Length != 0 : Hash256.Compute(Read(stream)) != target.Hash)
                        throw new ArtifactConflictException("Recovery candidate differs from its journal image");
                });
                if (candidate.Postimage != target) throw new ArtifactConflictException("Recovery candidate postimage is not authorized");
            }
            return Commit(active, candidates, direction, cancellation);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        { return Interrupted(active, direction, error, cancellation); }
        finally { PreparedWorldPlan.Cleanup(candidates); }
    }
    public ApplyReceipt? ReadReceipt(Hash256 planHash, long? revision = null)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        var active = Load(planHash); if (active is null) return null;
        while (true)
        {
            var receipt = Receipt(active);
            if (revision is null || receipt?.Revision == revision) return receipt;
            if (active.Journal.PreviousHash is not { } previous) throw new KeyNotFoundException("Receipt revision is not present in this journal history");
            var record = ApplyJournalJson.Read(Text(Object(previous)));
            if (record.Binding != active.Journal.Binding || record.HeaderHash != active.Journal.HeaderHash || record.Sequence != active.Journal.Sequence - 1)
                throw new InvalidDataException("Journal history has an inconsistent predecessor");
            active = new(active.Header, record, previous);
        }
    }
    public WorldApplyStatus? ReadStatus(Hash256 planHash)
    {
        var active = Load(planHash);
        return active is null ? null : new(active.Header.Plan.PlanHash, active.Journal.Direction, active.Journal.Phase, active.Journal.Sequence,
            active.Header.BackupManifestHash, Receipt(active));
    }
    private ApplyReceipt Commit(LoadedJournal active, IReadOnlyList<IArtifactCandidate> candidates, ApplyDirection direction, CancellationToken cancellation)
    {
        if (active.Header.AccessBinding != Access()) throw new ArtifactConflictException("Journal target root differs from the current write authority");
        foreach (var candidate in candidates) candidate.Verify(_ => { });
        var images = Images(active.Header);
        if (!Known(active.Header, images)) return Finish(active, images, direction, "Source image differs before commit", cancellation);
        active = Advance(active, direction, ApplyJournalPhase.Committing, 0, active.Journal.ReceiptHash);
        for (var index = 0; index < candidates.Count; index++)
        {
            WorldReferenceMembers.Catalog(inventory, active.Header.Plan);
            if (active.Header.Plan.Operations.Any(operation => operation is ReferencePatch))
            {
                images = Images(active.Header);
                if (!Known(active.Header, images)) return Finish(active, images, direction, "Reference source changed between commits", cancellation);
            }
            cancellation.ThrowIfCancellationRequested(); candidates[index].Commit();
            active = Advance(active, direction, ApplyJournalPhase.Committing, index + 1, active.Journal.ReceiptHash);
        }
        WorldReferenceMembers.Catalog(inventory, active.Header.Plan);
        images = Images(active.Header);
        active = Advance(active, direction, ApplyJournalPhase.ReadBack, candidates.Count, active.Journal.ReceiptHash);
        return Finish(active, images, direction, "Readback did not complete every promised output", cancellation);
    }
    private ApplyReceipt Interrupted(LoadedJournal original, ApplyDirection direction, Exception error, CancellationToken cancellation)
    {
        var current = Load(original.Header.Plan.PlanHash) ?? throw new InvalidDataException("Active journal disappeared after a commit attempt", error);
        if (current.Journal.Phase == ApplyJournalPhase.Completed && current.Journal.Direction == direction && Receipt(current) is { } saved) return saved;
        return Finish(current, Images(current.Header), direction, error.Message, cancellation, dependenciesVerified: false);
    }
    private static void Offline(bool acknowledged)
    { if (!acknowledged) throw new ArgumentException("World apply/recovery requires an offline-world acknowledgement", nameof(acknowledged)); }
    private static byte[] Read(Stream stream)
    {
        if (stream.Length > 67_108_864) throw new IOException("Journal object exceeds byte budget");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}

public sealed record WorldApplyStatus(Hash256 PlanHash, ApplyDirection Direction, ApplyJournalPhase Phase, long Sequence,
    Hash256 BackupManifestHash, ApplyReceipt? Receipt);
