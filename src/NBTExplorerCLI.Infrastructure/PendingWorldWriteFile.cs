using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Infrastructure;

internal sealed class PendingWorldWriteFile : IPendingWorldWrite
{
    private const int RecordLength = 3 + 4 * 64 + 3;
    private static readonly ArtifactFileIo Io = new();
    private readonly GuardedArtifactPath path;
    private readonly Guid world;
    private readonly object gate;
    private FileStream? file;
    private bool disposed;
    public PendingWorldWrite? Current { get; private set; }

    private PendingWorldWriteFile(GuardedArtifactPath path, Guid world, object gate)
    {
        (this.path, this.world, this.gate) = (path, world, gate);
        Read();
    }

    internal static IPendingWorldWrite Acquire(string root, Guid world, ArtifactWriterLock writer, object gate)
    {
        Monitor.Enter(gate);
        GuardedArtifactPath? path = null;
        try
        {
            path = GuardedArtifactPath.AcquirePendingWrite(root);
            path.HoldWriter(writer);
            return new PendingWorldWriteFile(path, world, gate);
        }
        catch
        {
            try { path?.Dispose(); } finally { Monitor.Exit(gate); }
            throw;
        }
    }

    public void Begin(PendingWorldWrite write)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Current is not null) throw new InvalidOperationException("A pending world write already exists");
        var directory = Path.GetDirectoryName(path.Target)!;
        using (var target = GuardedArtifactPath.AcquirePendingWrite(directory))
        using (var candidate = new ArtifactCandidate(target, new(world, PendingWorldWrite.FileName, ArtifactKind.Text), ArtifactImage.Absent, Encode(write), Io))
        {
            candidate.Flush();
            candidate.Verify(_ => { });
            candidate.Commit();
        }
        Read();
        if (Current != write) throw new IOException("The pending write marker differs after publication");
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (file is null) return;
        Io.Delete(file);
        file.Dispose();
        file = null;
        Current = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { file?.Dispose(); } finally { try { path.Dispose(); } finally { Monitor.Exit(gate); } }
    }

    private void Read()
    {
        FileStream opened;
        try { opened = GuardedArtifactPath.OpenFile(path.Target, FileMode.Open, FileAccess.Read, FileShare.None, deleteAccess: true); }
        catch (FileNotFoundException) { return; }
        try
        {
            if (opened.Length != RecordLength) throw new InvalidDataException("Invalid pending write marker length");
            Span<byte> bytes = stackalloc byte[RecordLength];
            opened.ReadExactly(bytes);
            var fields = Encoding.ASCII.GetString(bytes).Split(' ');
            if (fields.Length != 5 || fields[0] != "v1") throw new InvalidDataException("Invalid pending write marker");
            var marker = new PendingWorldWrite(new(fields[1]), new(fields[2]), new(fields[3]), new(fields[4]));
            if (!bytes.SequenceEqual(Encode(marker))) throw new InvalidDataException("Invalid pending write marker encoding");
            (file, Current) = (opened, marker);
        }
        catch (ArgumentException error)
        { opened.Dispose(); throw new InvalidDataException("Invalid pending write marker hash", error); }
        catch { opened.Dispose(); throw; }
    }

    private static byte[] Encode(PendingWorldWrite write)
        => Encoding.ASCII.GetBytes($"v1 {write.PlanHash.Hex} {write.WorldBinding.Hex} {write.StoreBinding.Hex} {write.Anchor.Hex}");
}
