using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class ApplyJournalContractTests
{
    public static void HeaderAndProgressBindImmutableContentAndHistory()
    {
        var plan = PlanTests.Plan(); var prepared = new PreparedManifest(plan, [PlanTests.Candidate()]);
        var header = new ApplyJournalHeader(plan, prepared, PlanTests.Preimage, PlanTests.Postimage);
        var headerJson = ApplyJournalHeaderJson.Write(header); var headerHash = Hash256.Compute(Encoding.UTF8.GetBytes(headerJson));
        var loaded = ApplyJournalHeaderJson.Read(headerJson);
        Require(loaded.Plan.PlanHash == plan.PlanHash && loaded.Prepared.ManifestHash == prepared.ManifestHash && loaded.BackupManifestHash == PlanTests.Preimage && loaded.AccessBinding == PlanTests.Postimage,
            "journal header retains exact semantic plan, frozen candidate manifest and verified preimage backup binding");
        var binding = JournalBinding.From(prepared);
        var first = new ApplyJournal(binding, headerHash, 0, null, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null);
        var firstJson = ApplyJournalJson.Write(first); var firstHash = Hash256.Compute(Encoding.UTF8.GetBytes(firstJson));
        var expected = $$$"""{"schemaFamily":"nbtcli.apply-journal","schemaVersion":1,"data":{"binding":{"planHash":"{{{binding.PlanHash.Hex}}}","manifestHash":"{{{binding.ManifestHash.Hex}}}"},"headerHash":"{{{headerHash.Hex}}}","sequence":0,"previousHash":null,"direction":"forward","phase":"prepared","completedCount":0,"receiptHash":null}}""";
        Require(firstJson == expected, "version 1 journal field names, order, nulls and enum spellings match the independent wire contract");
        var committing = new ApplyJournal(binding, headerHash, 1, firstHash, ApplyDirection.Forward, ApplyJournalPhase.Committing, 1, null);
        var roundtrip = ApplyJournalJson.Read(ApplyJournalJson.Write(committing));
        Require(roundtrip.Binding == binding && roundtrip.HeaderHash == headerHash && roundtrip.Sequence == 1 && roundtrip.PreviousHash == firstHash
            && roundtrip.CompletedCount == 1 && roundtrip.ReceiptHash is null, "small progress records bind the immutable header and prior content identity without copying whole plans per file");
        Require(ApplyJournalJson.Write(ApplyJournalJson.Read(firstJson)) == firstJson && ApplyJournalHeaderJson.Write(loaded) == headerJson, "journal/header bytes have deterministic bounded canonical roundtrips");
        Reject<NotSupportedException>(() => ApplyJournalJson.Read(firstJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Reject<JsonException>(() => ApplyJournalJson.Read(firstJson.Replace("\"data\":{", "\"data\":{\"unknown\":true,", StringComparison.Ordinal)));
        Reject<JsonException>(() => ApplyJournalHeaderJson.Read(headerJson.Replace("\"data\":{", "\"data\":{\"unknown\":true,", StringComparison.Ordinal)));
    }
    public static void AmbiguousBindingsAndInvalidProgressAreRejected()
    {
        var plan = PlanTests.Plan(); var prepared = new PreparedManifest(plan, [PlanTests.Candidate()]); var binding = JournalBinding.From(prepared);
        var different = PlanTests.Plan(policy: "different");
        Reject<ArgumentException>(() => new ApplyJournalHeader(different, prepared, PlanTests.Preimage, PlanTests.Postimage));
        Reject<ArgumentNullException>(() => new ApplyJournalHeader(plan, prepared, null!, PlanTests.Postimage));
        Reject<ArgumentNullException>(() => new ApplyJournalHeader(plan, prepared, PlanTests.Preimage, null!));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 0, PlanTests.Postimage, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 1, null, ApplyDirection.Forward, ApplyJournalPhase.Committing, 0, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 1, PlanTests.Postimage, ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 0, null, ApplyDirection.Rollback, ApplyJournalPhase.Prepared, 0, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 1, PlanTests.Postimage, ApplyDirection.Forward, ApplyJournalPhase.Completed, 1, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 1, PlanTests.Postimage, ApplyDirection.Forward, ApplyJournalPhase.Committing, 100_001, null));
        Reject<ArgumentException>(() => new ApplyJournal(binding, PlanTests.Preimage, 1, PlanTests.Postimage, (ApplyDirection)99, ApplyJournalPhase.Committing, 0, null));
    }
}
