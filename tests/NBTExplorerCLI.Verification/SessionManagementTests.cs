using System.Collections.Immutable;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.SessionExchangeTests;
using static NBTExplorerCLI.Verification.TranslationSessionTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionManagementTests
{
    public static void NavigationNotesAndResetPreserveHistory()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemTranslationSessionStore(root); var service = new TranslationSessionService(store); service.Start(StartRequest() with { Policy = new(1) }, Guid.NewGuid());
            var original = service.Read(SessionId); var jump = new SessionCommand(Guid.NewGuid(), SessionId, 0, SessionCommandKind.Jump, Page: 1); service.Execute(jump);
            Require(service.Status(SessionId) is { CurrentPage: 1, Pending: 2, Translated: 0 } && service.Read(SessionId).Source.SnapshotId == original.Source.SnapshotId, "jump moves navigation only, preserving completion and immutable sources");
            var note = new SessionCommand(Guid.NewGuid(), SessionId, 1, SessionCommandKind.Note, SlotId: original.Slots[0].Slot.Id.Digest, Text: "註記 {{T0}}", Origin: "human", Confirmed: true); service.Execute(note);
            var glossary = new SessionCommand(Guid.NewGuid(), SessionId, 2, SessionCommandKind.Glossary, Source: "Name", Text: "名稱", Origin: "human", Confirmed: true); service.Execute(glossary);
            var page = service.Export(SessionId, 0); var input = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            input = input with { Slots = [input.Slots[0] with { State = SessionReplyState.Translated, Text = "翻譯 %s" }] };
            service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(input)));
            var id = original.Slots[0].Slot.Id.Digest; var preview = service.PreviewReset(SessionId, [id]); var reset = new SessionCommand(Guid.NewGuid(), SessionId, preview.Revision, SessionCommandKind.Reset, Confirmed: true, ResetSlots: preview.Slots);
            var receipt = service.Execute(reset); var state = new TranslationSessionService(new FileSystemTranslationSessionStore(root)).Read(SessionId);
            Require(preview.TranslationsRetainedInHistory == 1 && state.Slots.All(item => item.State == SessionSlotState.Pending) && state.Workspace!.CurrentPage == 1
                && state.Workspace.Notes.Single().Text == note.Text && state.Workspace.Glossary.Single().Translation == glossary.Text
                && state.Operations.Any(item => item.OperationId == input.OperationId && item.InputBase64 is not null), "explicit scoped reset retains all prior reply inputs, notes, glossary, navigation and source data");
            Require(service.Execute(reset).InputHash == receipt.InputHash && service.Read(SessionId).Revision == receipt.Revision, "management operations have durable same-input replay without duplicate effects");
            Require(service.Advance(SessionId).Page == 1 && service.Get(SessionId, id).Slot.SourceText == original.Slots[0].Slot.SourceText, "advance starts at navigation cursor and get returns a single exact slot");
            CandidateIoTests.Reject<ArgumentException>(() => service.Execute(reset with { OperationId = Guid.NewGuid(), Revision = state.Revision, Confirmed = false }));
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ContextOrderAndAdvisoryMemory()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(root)); var request = StartRequest();
            TextSlot Page(int index, string context = "book.page") => new(new(request.Slots[0].Address.Artifact, new([PathSegment.ForKey("Inventory"), PathSegment.ForIndex(0), PathSegment.ForKey("pages"), PathSegment.ForIndex(index)]), "book.page"), "Same", context, "en_us", "zh_tw", false, PlaceholderPolicy.None);
            var slots = new[] { Page(10), Page(2), Page(1) }; service.Start(request with { Slots = slots.ToImmutableArray() }, Guid.NewGuid());
            var group = service.Groups(SessionId).Single();
            Require(group.Kind == "book.pages" && group.Slots.SequenceEqual(new[] { slots[2].Id.Digest, slots[1].Id.Digest, slots[0].Id.Digest })
                && group.MemberDigest == SourceIdentity.Members(group.GroupId, new[] { slots[2].Id, slots[1].Id, slots[0].Id }, 0), "group member identity preserves numeric book page order and each complete occurrence address");
            var page = service.Export(SessionId, 0); var reply = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            reply = reply with { Slots = reply.Slots.Select((slot, index) => slot with { State = SessionReplyState.Translated, Text = "Translation" + index }).ToImmutableArray() }; service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
            var other = Guid.NewGuid(); var targetSlot = Page(20); service.Start(request with { SessionId = other, Slots = [targetSlot] }, Guid.NewGuid());
            var memory = service.Memory(other, targetSlot.Id.Digest, [SessionId], "glossary1");
            Require(memory.Length == 3 && memory.Select(item => item.Text).Distinct().Count() == 3 && memory.All(item => item.SessionId == SessionId && item.Revision == 1 && item.Provenance.Length > 0)
                && service.Get(other, targetSlot.Id.Digest).State == SessionSlotState.Pending, "same-context memory entries retain provenance as independent candidates and never force a translation");
            var different = Guid.NewGuid(); var differentSlot = Page(21, "sign.front.line"); service.Start(request with { SessionId = different, Slots = [differentSlot] }, Guid.NewGuid());
            Require(service.Memory(different, differentSlot.Id.Digest, [SessionId], "glossary1").IsEmpty, "same source text with a different context has no automatic memory match");
            Require(service.Memory(other, targetSlot.Id.Digest, [SessionId], "glossary2").All(item => item.Key != memory[0].Key), "glossary version participates in memory identity");
            Require(service.Groups(SessionId).Single().MemberDigest == group.MemberDigest, "translation revisions do not mutate fixed source group membership");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void StyledFragmentsPreserveComponentOrder()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var request = StartRequest(); var artifact = new ArtifactAddress(World, "packs/p/a.json", ArtifactKind.Json);
            TextSlot Fragment(TypedPath path, string text) => new(new(artifact, path, "advancement.title"), text, "advancement.title", "en_us", "zh_tw", false, PlaceholderPolicy.None);
            var title = new[] { PathSegment.ForKey("display"), PathSegment.ForKey("title") };
            var first = Fragment(new(title.Append(PathSegment.ForKey("text"))), "Root");
            var tail = Fragment(new(title.Concat(new[] { PathSegment.ForKey("extra"), PathSegment.ForIndex(0), PathSegment.ForKey("text") })), "Tail");
            var sources = ImmutableArray.Create(new ManifestSource(artifact, Hash256.Compute([1]), SourceOutcome.Success)); var source = request.Source with { Sources = sources, SnapshotId = SourceIdentity.Snapshot(World, sources, SnapshotScope.All, request.Source.Versions) };
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(root)); service.Start(request with { Source = source, Slots = [first, tail] }, Guid.NewGuid());
            Require(service.Groups(SessionId).Single().Slots.SequenceEqual(new[] { first.Id.Digest, tail.Id.Digest }), "styled root text precedes extra fragments in semantic component order, not alphabetical property order");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ManagementRejectsStaleTamperedAndUnknownScope()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemTranslationSessionStore(root); var service = new TranslationSessionService(store); service.Start(StartRequest(), Guid.NewGuid());
            var command = new SessionCommand(Guid.NewGuid(), SessionId, 0, SessionCommandKind.Note, Text: "history", Origin: "user"); service.Execute(command); var before = store.Read(SessionId)!;
            CandidateIoTests.Reject<ArtifactConflictException>(() => service.Execute(command with { Text = "changed operation" }));
            CandidateIoTests.Reject<ArtifactConflictException>(() => service.Execute(command with { OperationId = Guid.NewGuid() }));
            CandidateIoTests.Reject<ArgumentException>(() => service.Execute(new(Guid.NewGuid(), SessionId, 1, SessionCommandKind.Jump, Page: 100)));
            CandidateIoTests.Reject<ArgumentException>(() => service.PreviewReset(SessionId, [Hash256.Compute([99])]));
            CandidateIoTests.Reject<KeyNotFoundException>(() => service.Get(SessionId, Hash256.Compute([99])));
            Require(store.Read(SessionId)!.SequenceEqual(before), "unknown targets, stale revisions and changed operation inputs cannot save or discard existing notes");
            var state = service.Read(SessionId);
            CandidateIoTests.Reject<System.Text.Json.JsonException>(() => TranslationSessionJson.Write(state with { Workspace = state.Workspace! with { Notes = [] } }));
            var all = service.Export(SessionId, 0); var reply = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(all, Guid.NewGuid())); service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
            var status = service.Status(SessionId);
            Require(status.Unchanged == 2 && status.Translated == 0 && status.Pending + status.Translated + status.Unchanged + status.Applied + status.Conflict == status.Total, "progress counts pending, validated translation, explicit unchanged, conflicts and actual apply status separately");
            CandidateIoTests.Reject<InvalidOperationException>(() => service.Advance(SessionId));
        }
        finally { Directory.Delete(root, true); }
    }
}
