namespace Next.Application;

internal sealed class ScanBufferBudget(long maximum)
{
    private readonly object gate = new();
    private long reserved, peak;
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal long Peak { get { lock (gate) return peak; } }
    internal async ValueTask<IDisposable> AcquireAsync(long bytes, CancellationToken token)
    {
        if (bytes < 0 || bytes > maximum) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (true)
        {
            token.ThrowIfCancellationRequested(); Task wait;
            lock (gate)
            {
                if (bytes <= maximum - reserved)
                {
                    reserved += bytes; peak = Math.Max(peak, reserved); return new Lease(this, bytes);
                }
                wait = changed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }
    private void Release(long bytes)
    {
        lock (gate)
        {
            reserved -= bytes; var previous = changed; changed = new(TaskCreationOptions.RunContinuationsAsynchronously); previous.TrySetResult();
        }
    }
    private sealed class Lease(ScanBufferBudget owner, long bytes) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(bytes); }
    }
}
