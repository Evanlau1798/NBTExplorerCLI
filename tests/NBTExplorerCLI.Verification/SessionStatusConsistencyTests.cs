using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Cli;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionStatusConsistencyTests
{
    public static void TheStatusBranchUsesItsCapturedSessionDuringAnInterleavedReset()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(world, System.IO.Path.Combine(work, "cache"));
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(System.IO.Path.Combine(work, "sessions"))); var id = Guid.NewGuid();
            service.Start(snapshot, id, "en_us", "zh_tw", Guid.NewGuid()); var page = service.Export(id, 0);
            var reply = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = SessionReplyState.Translated, Text = "translated" }).ToImmutableArray() };
            service.Submit(id, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply))); var translated = service.Read(id);
            var inventory = new WorldInventory(world, snapshot.Manifest.WorldId); var storage = new ArtifactStorage(world, snapshot.Manifest.WorldId);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, translated.Slots.Select((slot, index) => new LiteralPatch("slot-" + index, new(slot.Slot, slot.Reply!))));
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(System.IO.Path.Combine(work, "objects")), new FileSystemApplyJournalStore(System.IO.Path.Combine(work, "journals")));
            Require(engine.Apply(prepared, true).Success, "control: a real complete world apply precedes the captured sealed state");
            service.Seal(id, translated.Revision, Guid.NewGuid(), plan.PlanHash, engine); var captured = service.Read(id);
            Require(captured.Slots.All(slot => slot.State == SessionSlotState.Applied), "capture a real replay-valid Applied session");
            service.Execute(new(Guid.NewGuid(), id, captured.Revision, SessionCommandKind.Reset, Confirmed: true,
                ResetSlots: captured.Slots.Select(slot => slot.Slot.Id.Digest).ToImmutableArray()));
            Require(service.Status(id).Pending == captured.Slots.Length, "control: the durable session now contains reset/pending work");
            var method = typeof(CliApplication).Assembly.GetType("NBTExplorerCLI.Cli.SessionCommands", true)!.GetMethod("Status", BindingFlags.Static | BindingFlags.NonPublic)!;
            var result = method.Invoke(null, [null, captured])!;
            var data = result.GetType().GetProperty("Data")!.GetValue(result)!;
            var status = (SessionStatus)data.GetType().GetProperty("status")!.GetValue(data)!;
            Require(status.Applied == captured.Slots.Length && status.Pending == 0 && status.Revision == captured.Revision,
                "the actual sealed-status branch must project the captured Applied snapshot, not re-read reset counts");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
