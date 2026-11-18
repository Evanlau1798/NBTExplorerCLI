using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.SessionExchangeTests;
using static NBTExplorerCLI.Verification.TranslationSessionTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionBoundaryTests
{
    public static void InvalidPagesAndTamperedStateDoNotAdvance()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemTranslationSessionStore(root); var service = new TranslationSessionService(store); service.Start(StartRequest(), Guid.NewGuid());
            var head = store.Read(SessionId)!; var page = service.Export(SessionId, 0); var reply = Reply(page);
            var shortReply = reply with { Slots = [reply.Slots[0]] };
            Reject<SessionReplyRejectedException>(() => service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(shortReply))));
            Require(store.Read(SessionId)!.SequenceEqual(head) && !service.ValidatedPage(SessionId, 0).Valid, "missing slots neither advance the revision nor create a validated page");
            var stale = reply with { Revision = 1 };
            Reject<SessionReplyRejectedException>(() => service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(stale))));
            Require(store.Read(SessionId)!.SequenceEqual(head), "stale source/page revision leaves the complete old session intact");
            service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
            var translated = service.Read(SessionId); var altered = translated with { Slots = translated.Slots.SetItem(0, translated.Slots[0] with { Reply = translated.Slots[0].Reply! with { Text = "not the retained reply %s" } }) };
            Reject<JsonException>(() => TranslationSessionJson.Write(altered));
            var changedSource = translated.Source with { SnapshotId = SourceIdentity.Snapshot(World, [], SnapshotScope.All, translated.Source.Versions) };
            Reject<JsonException>(() => TranslationSessionJson.Write(translated with { Source = changedSource }));
            var malformed = JsonNode.Parse(TranslationSessionJson.Write(translated))!; malformed["schemaVersion"] = 99;
            Reject<NotSupportedException>(() => TranslationSessionJson.Read(malformed.ToJsonString()));
        }
        finally { Directory.Delete(root, true); }
    }
    public static void SerializedPagingAndOversizedSlot()
    {
        var root = ScratchDirectory.CreateChild(); var oversized = ScratchDirectory.CreateChild();
        try
        {
            var request = StartRequest(); var source = request.Slots[0];
            var slots = Enumerable.Range(0, 7).Select(index => new TextSlot(Address(new([PathSegment.ForIndex(index)])), new string('中', 200), "page", "en_us", "zh_tw", false, PlaceholderPolicy.None)).ToImmutableArray();
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(root));
            service.Start(request with { Slots = slots, Policy = new(7, 4500) }, Guid.NewGuid());
            var state = service.Read(SessionId);
            Require(state.Pages.Length > 1 && state.Pages.Sum(page => page.Length) == slots.Length, "pages use actual encoded payload size rather than item count or character count");
            for (var index = 0; index < state.Pages.Length; index++)
                Require(Encoding.UTF8.GetByteCount(SessionExchangeJson.Prompt(service.Export(SessionId, index), Guid.NewGuid())) <= state.Policy.MaximumPageBytes, "every full source/template payload fits the selected byte policy");
            var big = new TextSlot(source.Address, new string('中', 20_000), source.Context, "en_us", "zh_tw", false, PlaceholderPolicy.None);
            var other = new FileSystemTranslationSessionStore(oversized);
            Reject<SessionPageOversizedException>(() => new TranslationSessionService(other).Start(request with { Slots = [big], Policy = new(30, 4500) }, Guid.NewGuid()));
            Require(other.Read(SessionId) is null, "a single unalignable oversized slot produces an explicit outcome without a partial saved session");
        }
        finally { Directory.Delete(root, true); Directory.Delete(oversized, true); }
    }
    private sealed class FailedFlush : ArtifactFileIo
    {
        public override void Flush(FileStream output) => throw new IOException("Injected session flush failure");
    }
    public static void FlushFailureAndForeignRootPreserveInputs()
    {
        var root = ScratchDirectory.CreateChild(); var foreign = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemTranslationSessionStore(root); var service = new TranslationSessionService(store); service.Start(StartRequest(), Guid.NewGuid());
            var original = store.Read(SessionId)!; var page = service.Export(SessionId, 0); var bytes = Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(Reply(page)));
            var replyFile = Path.Combine(Path.GetDirectoryName(root)!, "retained-reply-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllBytes(replyFile, bytes);
            var failing = new TranslationSessionService(new FileSystemTranslationSessionStore(root, new FailedFlush()));
            Reject<IOException>(() => failing.Submit(SessionId, File.ReadAllBytes(replyFile)));
            Require(store.Read(SessionId)!.SequenceEqual(original) && File.ReadAllBytes(replyFile).SequenceEqual(bytes) && Directory.GetFiles(root, "*.candidate").Length == 0,
                "flush failure preserves the old complete session and exact reply input and releases its temporary candidate");
            service.Submit(SessionId, bytes);
            Require(File.ReadAllBytes(replyFile).SequenceEqual(bytes), "successful persistence also retains the original reply file");
            File.WriteAllBytes(Path.Combine(foreign, "user.json"), [1, 2, 3]);
            Reject<IOException>(() => new FileSystemTranslationSessionStore(foreign));
            Require(Directory.GetFiles(foreign).Length == 1 && File.ReadAllBytes(Path.Combine(foreign, "user.json")).SequenceEqual(new byte[] { 1, 2, 3 }), "session store refuses an unowned foreign directory without replacing user files");
        }
        finally { Directory.Delete(root, true); Directory.Delete(foreign, true); }
    }
}
