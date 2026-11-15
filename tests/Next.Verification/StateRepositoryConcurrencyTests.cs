using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class StateRepositoryConcurrencyTests
{
    internal sealed class InterleavedReplace : ArtifactFileIo, IDisposable
    {
        private readonly ManualResetEventSlim second = new(), firstCompleted = new();
        private int entered, firstThread;
        public override void Replace(string candidate, string target, bool absent)
        {
            if (Interlocked.Increment(ref entered) == 1)
            {
                firstThread = Environment.CurrentManagedThreadId;
                _ = second.Wait(TimeSpan.FromSeconds(2));
            }
            else
            {
                second.Set(); Require(firstCompleted.Wait(TimeSpan.FromSeconds(5)), "first write must complete readback before the second replace");
            }
            base.Replace(candidate, target, absent);
        }
        internal void Completed() { if (Environment.CurrentManagedThreadId == firstThread) firstCompleted.Set(); }
        public void Dispose() { second.Dispose(); firstCompleted.Dispose(); }
    }
    public static void SharedCacheStoreCasIsAtomic()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            byte[] original = [0], first = [1], second = [2];
            var initial = new FileSystemScanCacheStore(root); initial.PublishManifest(original, null);
            using var io = new InterleavedReplace(); var store = new FileSystemScanCacheStore(root, io); var successes = 0;
            Task Attempt(byte[] bytes) => Task.Factory.StartNew(() =>
            {
                try { store.PublishManifest(bytes, Hash256.Compute(original)); Interlocked.Increment(ref successes); }
                catch (IOException) { }
                finally { io.Completed(); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task.WaitAll(Attempt(first), Attempt(second));
            Require(successes == 1, "one shared cache repository must not commit two different manifests against the same expected head");
            var final = store.ReadManifest()!;
            Require(final.SequenceEqual(first) || final.SequenceEqual(second), "exactly one complete manifest remains and losing CAS does not overwrite it");
        }
        finally { Directory.Delete(root, true); }
    }
}
