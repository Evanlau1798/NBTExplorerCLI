using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

internal sealed class ArtifactCandidate : IArtifactCandidate
{
    private readonly GuardedArtifactPath path;
    private readonly ArtifactFileIo io;
    private readonly ArtifactImage stagedImage;
    private bool flushed, verified, committed, disposed, attempted;
    public string CandidatePath { get; }
    public ArtifactImage Preimage { get; }
    public ArtifactImage Postimage { get; }
    public ArtifactCandidate(GuardedArtifactPath path, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, ArtifactFileIo io, bool delete = false)
    {
        this.path = path; this.io = io; Preimage = preimage; stagedImage = ArtifactImage.Present(Hash256.Compute(bytes.Span));
        Postimage = delete ? ArtifactImage.Absent : stagedImage;
        var current = path.HoldParents(create: false) ? ArtifactStorage.Image(path.Target) : ArtifactImage.Absent;
        if (current != preimage) throw new ArtifactConflictException("Source differs from expected preimage");
        CandidatePath = Path.Combine(path.StagingDirectory, ".nbtcli-" + Guid.NewGuid().ToString("N") + ".candidate");
        var created = false;
        try
        {
            using var output = GuardedArtifactPath.OpenFile(CandidatePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true;
            io.Write(output, bytes);
        }
        catch { if (created) File.Delete(CandidatePath); throw; }
    }
    public void Flush()
    {
        RequireActive(); verified = false; flushed = false;
        using var stream = GuardedArtifactPath.OpenFile(CandidatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        io.Flush(stream); flushed = true;
    }
    public void Verify(Action<Stream> verify)
    {
        RequireActive(); ArgumentNullException.ThrowIfNull(verify); verified = false;
        if (!flushed) throw new InvalidOperationException("Candidate must be flushed before verification");
        var bytes = ArtifactStorage.ReadFile(CandidatePath);
        if (ArtifactImage.Present(Hash256.Compute(bytes)) != stagedImage) throw new ArtifactConflictException("Candidate differs from staged image");
        using var input = new MemoryStream(bytes, writable: false); verify(input);
        verified = true;
    }
    public void Commit()
    {
        RequireActive();
        if (!verified) throw new InvalidOperationException("Commit requires a verified candidate");
        if (ArtifactStorage.Image(CandidatePath) != stagedImage) throw new ArtifactConflictException("Candidate changed after verification");
        var parents = path.HoldParents(create: false);
        using var source = parents ? OpenSource() : null;
        var current = source is not null ? ArtifactStorage.Image(source) : parents ? ArtifactStorage.Image(path.Target) : ArtifactImage.Absent;
        if (current != Preimage) throw new ArtifactConflictException("Source changed before commit");
        if (Preimage == Postimage) { File.Delete(CandidatePath); committed = true; return; }
        attempted = true;
        try
        {
            if (Postimage.ExpectedAbsent) io.Delete(source!);
            else { path.HoldParents(create: true); io.Replace(CandidatePath, path.Target, Preimage.ExpectedAbsent); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new ArtifactCommitException("Replace failed; inspect pre/postimages before recovery", error); }
        committed = true;
        if (Postimage.ExpectedAbsent) source?.Dispose();
        try
        {
            if (ArtifactStorage.Image(path.Target) != Postimage) throw new IOException("Committed image differs from expected postimage");
            if (Postimage.ExpectedAbsent) File.Delete(CandidatePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new ArtifactCommitException("Committed image failed readback; recovery required", error); }
    }
    private void RequireActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (committed || attempted) throw new InvalidOperationException("Candidate already committed or requires recovery");
    }
    private FileStream? OpenSource()
    {
        if (Preimage.ExpectedAbsent) return null;
        try { return GuardedArtifactPath.OpenFile(path.Target, FileMode.Open, FileAccess.Read,
            Postimage.ExpectedAbsent ? FileShare.Read : FileShare.Read | FileShare.Delete, deleteAccess: Postimage.ExpectedAbsent); }
        catch (FileNotFoundException) { throw new ArtifactConflictException("Expected source disappeared before commit"); }
    }
    public void Dispose()
    {
        if (disposed) return;
        try { if (!committed && !attempted) File.Delete(CandidatePath); }
        finally { disposed = true; path.Dispose(); }
    }
}
