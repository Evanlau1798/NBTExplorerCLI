namespace NBTExplorerCLI.Infrastructure;

internal sealed class ArtifactWriterLock(string root)
{
    private readonly object gate = new();
    private FileStream? stream;
    private int references;
    public IDisposable Acquire()
    {
        lock (gate)
        {
            stream ??= GuardedArtifactPath.OpenFile(Path.Combine(root, ".nbtcli-writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            references++; return new Lease(this);
        }
    }
    private void Release()
    {
        lock (gate)
        {
            if (--references != 0) return;
            stream!.Dispose(); stream = null;
        }
    }
    private sealed class Lease(ArtifactWriterLock owner) : IDisposable
    {
        private ArtifactWriterLock? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release();
    }
}
