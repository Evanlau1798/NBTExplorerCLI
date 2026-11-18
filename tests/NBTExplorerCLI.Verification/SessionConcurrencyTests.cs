using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.SessionExchangeTests;
using static NBTExplorerCLI.Verification.TranslationSessionTests;

namespace NBTExplorerCLI.Verification;

internal static class SessionConcurrencyTests
{
    public static void SameStoreCannotCommitSameRevisionTwice()
    {
        var root = ScratchDirectory.CreateChild(); var firstRoot = ScratchDirectory.CreateChild(); var secondRoot = ScratchDirectory.CreateChild();
        try
        {
            var operation = Guid.NewGuid(); var request = StartRequest();
            var initial = new FileSystemTranslationSessionStore(root); new TranslationSessionService(initial).Start(request, operation); var head = Hash256.Compute(initial.Read(SessionId)!);
            var first = Candidate(firstRoot, "First %s"); var second = Candidate(secondRoot, "Second %s");
            using var io = new StateRepositoryConcurrencyTests.InterleavedReplace(); var store = new FileSystemTranslationSessionStore(root, io); var successes = 0;
            Task Attempt(byte[] bytes) => Task.Factory.StartNew(() =>
            {
                try { store.Write(SessionId, bytes, head); Interlocked.Increment(ref successes); }
                catch (IOException) { }
                finally { io.Completed(); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var tasks = new[] { Attempt(first), Attempt(second) }; Task.WaitAll(tasks);
            Require(successes == 1, "concurrent calls through one store cannot both replace the same expected revision and lose an accepted operation");
            var final = store.Read(SessionId)!;
            Require(final.SequenceEqual(first) || final.SequenceEqual(second), "the only committed head is one complete verified session candidate");
            byte[] Candidate(string directory, string text)
            {
                var target = new FileSystemTranslationSessionStore(directory); var service = new TranslationSessionService(target); service.Start(request, operation);
                var page = service.Export(SessionId, 0); var reply = Reply(page); reply = reply with { Slots = reply.Slots.SetItem(0, reply.Slots[0] with { Text = text }) };
                service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply))); return target.Read(SessionId)!;
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(firstRoot, true); Directory.Delete(secondRoot, true); }
    }
}
