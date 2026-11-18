using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class DurableReceiptBudgetTests
{
    public static void ErrorDiagnosticsCannotExhaustTheMachineOutputBudget()
    {
        var content = new string('\u00e9', 16_000);
        var output = CommandOutputJson.Write("batch", new { content }, "Failed", "INVALID_DATA", "Invalid data", new string('\u00e9', 12_000_000));
        using var parsed = JsonDocument.Parse(output);
        Require(parsed.RootElement.GetProperty("data").GetProperty("content").GetString() == content
            && parsed.RootElement.GetProperty("error").GetProperty("details").GetString()!.Length <= 4096,
            "diagnostics are bounded without changing user content");
    }
    private sealed class FailureReason(IApplyJournalStore inner, string message) : IApplyJournalStore
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        internal bool Enabled { get; set; } = true;
        public byte[]? Read(Hash256 planHash) => inner.Read(planHash);
        public void Write(Hash256 planHash, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            var journal = ApplyJournalJson.Read(System.Text.Encoding.UTF8.GetString(bytes.Span));
            if (Enabled && journal.Phase == ApplyJournalPhase.Committing && journal.CompletedCount == 1) throw new IOException(message);
            inner.Write(planHash, bytes, expected);
        }
    }
    public static void InterruptedReceiptsHaveBoundedNonemptyReasons()
    {
        var failures = new List<string>();
        foreach (var message in new[] { "", new string('\ud800', 1_000) })
        {
            var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                var storage = new ArtifactStorage(root, World); var io = new FailureReason(new FileSystemApplyJournalStore(Path.Combine(state, "journals")), message);
                using var prepared = WorldApplyTests.Functions(root, state, storage);
                var engine = new WorldApplyEngine(new WorldInventory(root, World), storage, new FileSystemWorldBackupStore(Path.Combine(state, "objects")), io);
                ApplyReceipt? receipt = null;
                try { receipt = engine.Apply(prepared, true); }
                catch (Exception error) { failures.Add((message.Length == 0 ? "empty" : "long UTF16") + ":" + error.GetType().Name); }
                if (receipt is null) failures.Add("no interrupted receipt after first replacement");
                else
                {
                    if (receipt.Results.Any(result => result.Reason is not null && (string.IsNullOrWhiteSpace(result.Reason) || result.Reason.Length > 256))) failures.Add("unbounded or empty reason");
                    Require(engine.ReadReceipt(prepared.Plan.PlanHash) is not null && receipt.Counts[OperationOutcome.Applied] == 1, "actual partial NTFS apply has a readable receipt");
                }
                io.Enabled = false; Require(engine.Recover(prepared.Plan.PlanHash, true).Success, "later recovery remains possible after the diagnostic fault is removed");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
        Require(failures.Count == 0, "durable diagnostic contract: " + string.Join(" | ", failures));
    }
    public static void JournalHeadersRespectTheReaderTokenBudgetBeforePublication()
    {
        ApplyJournalHeader Header(int count)
        {
            var value = new NbtCompound(Enumerable.Range(0, 49).Select(index => new NbtEntry("field" + index, new NbtInt(index))));
            var operations = Enumerable.Range(0, count).Select(index =>
            {
                var path = new TypedPath([PathSegment.ForKey("item" + index)]);
                return new TypedPatch("edit-" + index, new(PlanTests.Artifact, path, "test.typed"), TypedEdit.Replace(path, value, value));
            });
            var plan = new PatchPlan(World, PlanTests.Snapshot, "test-profile", "test-policy", operations,
                [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage))], [new(PlanTests.Artifact, OutputIntent.Write, ["parse"])], []);
            var prepared = new PreparedManifest(plan, [new(PlanTests.Artifact, "stage/header.dat", ArtifactImage.Present(PlanTests.Preimage), [new("parse", true, PlanTests.Preimage)])]);
            return new(plan, prepared, PlanTests.Preimage, PlanTests.Preimage);
        }
        var small = Header(1); Require(ApplyJournalHeaderJson.Read(ApplyJournalHeaderJson.Write(small)).Plan.PlanHash == small.Plan.PlanHash, "small valid headers remain readable");
        var large = Header(1_000); using var plan = JsonDocument.Parse(PatchPlanJson.Write(large.Plan)); using var prepared = JsonDocument.Parse(PreparedManifestJson.Write(large.Prepared));
        var shape = new { plan = plan.RootElement, prepared = prepared.RootElement, backupManifestHash = large.BackupManifestHash, accessBinding = large.AccessBinding };
        var priorPrimitive = typeof(PatchPlanJson).Assembly.GetType("NBTExplorerCLI.Formats.ContractJson")!.GetMethod("WriteBounded", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(shape.GetType());
        var oldOutput = (string)priorPrimitive.Invoke(null, [ApplyJournalHeader.SchemaFamily, shape])!;
        Require(Encoding.UTF8.GetByteCount(oldOutput) < 67_108_864, "the old byte-only primitive accepts this header under 64 MiB");
        Reject<JsonException>(() => ApplyJournalHeaderJson.Read(oldOutput));
        Reject<JsonOutputLimitException>(() => ApplyJournalHeaderJson.Write(large));
    }
}
