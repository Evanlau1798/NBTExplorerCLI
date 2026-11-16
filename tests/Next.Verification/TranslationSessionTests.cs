using System.Collections.Immutable;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.SessionExchangeTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class TranslationSessionTests
{
    internal static SessionStartRequest StartRequest()
    {
        var slots = Slots(); var sources = ImmutableArray.Create(new ManifestSource(slots[0].Address.Artifact, Hash256.Compute([1]), SourceOutcome.Success));
        var versions = new InterpretationVersions("codec1", "profile1", "rules1", "policy1");
        var source = new SessionSourceSnapshot(World, SourceIdentity.Snapshot(World, sources, SnapshotScope.All, versions), sources, [], [], [], [], versions, true);
        return new(SessionId, source, "en_us", "zh_tw", new(), slots.ToImmutableArray());
    }
    public static void DiskSaveAndRestartIdempotency()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(root)); var request = StartRequest(); var operation = Guid.NewGuid();
            var start = service.Start(request, operation);
            Require(start.Revision == 0 && service.Read(SessionId).Slots.All(slot => slot.State == SessionSlotState.Pending), "start persists a bound immutable source snapshot and pending slots");
            var page = service.Export(SessionId, 0); var reply = Reply(page); var bytes = Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply));
            var receipt = service.Submit(SessionId, bytes);
            var restarted = new TranslationSessionService(new FileSystemTranslationSessionStore(root)); var state = restarted.Read(SessionId);
            Require(receipt.FromRevision == 0 && receipt.Revision == 1 && state.Revision == 1 && state.Slots.All(slot => slot.State == SessionSlotState.Translated)
                && state.Operations.Single(item => item.OperationId == reply.OperationId).InputBase64 == Convert.ToBase64String(bytes), "one atomic head retains exact reply input, validated text and successful receipt across restart");
            Require(Same(restarted.Submit(SessionId, bytes), receipt) && restarted.Read(SessionId).Revision == 1, "same operation/input replay returns its durable receipt before stale-revision validation");
            Require(Same(restarted.Start(request, operation), start) && restarted.Read(SessionId).Revision == 1, "start replay cannot reset a progressed session");
            var changed = reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { Text = "別的譯文 %s" }) };
            Reject<ArtifactConflictException>(() => restarted.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(changed))));
            Require(restarted.ValidatedPage(SessionId, 0).Valid, "later apply consumers obtain stored text through the same slot/page validator");
        }
        finally { Directory.Delete(root, true); }
    }
    private static bool Same(SessionOperationReceipt left, SessionOperationReceipt right) => left.OperationId == right.OperationId && left.InputHash == right.InputHash && left.FromRevision == right.FromRevision
        && left.Revision == right.Revision && left.Kind == right.Kind && left.InputBase64 == right.InputBase64 && left.Slots.SequenceEqual(right.Slots);
    public static void StrictStateRoundtripAndCas()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemTranslationSessionStore(root); var service = new TranslationSessionService(store); service.Start(StartRequest(), Guid.NewGuid());
            var original = store.Read(SessionId)!; var state = TranslationSessionJson.Read(Encoding.UTF8.GetString(original));
            Require(TranslationSessionJson.Write(state) == Encoding.UTF8.GetString(original) && state.Source.SnapshotId == StartRequest().Source.SnapshotId, "session schema roundtrips exact bound source and slot identities");
            store.Write(SessionId, original, Hash256.Compute(original));
            Reject<ArtifactConflictException>(() => store.Write(SessionId, original, null));
            Reject<ArtifactConflictException>(() => store.Write(SessionId, original, Hash256.Compute([99])));
            Reject<ArgumentException>(() => store.Write(Guid.NewGuid(), original, null));
            Require(store.Read(SessionId)!.SequenceEqual(original), "stale CAS or foreign session identity preserves the old complete head");
        }
        finally { Directory.Delete(root, true); }
    }
}
