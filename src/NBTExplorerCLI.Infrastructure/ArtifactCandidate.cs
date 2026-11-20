using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Infrastructure;

internal sealed class ArtifactCandidate : IArtifactCandidate
{
    private readonly GuardedArtifactPath path;
    private readonly ArtifactFileIo io;
    private readonly ArtifactImage stagedImage;
    private readonly ArtifactAddress artifact;
    private bool flushed, verified, committed, disposed, attempted;
    public string CandidatePath { get; }
    public ArtifactImage Preimage { get; }
    public ArtifactImage Postimage { get; }
    public CandidateCleanupProof? CleanupProof { get; }
    public ArtifactCandidate(GuardedArtifactPath path, ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, ArtifactFileIo io, bool delete = false, bool track = false)
    {
        this.artifact = artifact;
        using var observation = WorkDiagnostics.Start("candidate.write", artifact.RelativePath, "staging", artifact.WorldId);
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
            observation?.SetTag("bytes", (long)bytes.Length);
            if (track) CleanupProof = new(CandidatePath, GuardedArtifactPath.FileIdentity(output), stagedImage.Hash!);
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
        using var observation = WorkDiagnostics.Start("candidate.verify", artifact.RelativePath, "readback", artifact.WorldId);
        RequireActive(); ArgumentNullException.ThrowIfNull(verify); verified = false;
        if (!flushed) throw new InvalidOperationException("Candidate must be flushed before verification");
        using var input = GuardedArtifactPath.OpenFile(CandidatePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var actual = ArtifactStorage.Image(input);
        observation?.SetTag("bytes", input.Length);
        observation?.SetTag("comparisons", 1L);
        if (actual != stagedImage) throw new ArtifactConflictException("Candidate differs from staged image");
        input.Position = 0; verify(input);
        verified = true;
    }
    public void Commit() => Commit(null);

    internal void Commit(Action? validateDestination)
    {
        using var observation = WorkDiagnostics.Start("candidate.commit", artifact.RelativePath, "commit", artifact.WorldId);
        RequireActive();
        if (!verified) throw new InvalidOperationException("Commit requires a verified candidate");
        if (ArtifactStorage.Image(CandidatePath) != stagedImage) throw new ArtifactConflictException("Candidate changed after verification");
        var parents = path.HoldParents(create: false);
        using var source = parents ? OpenSource() : null;
        var current = source is not null ? ArtifactStorage.Image(source) : parents ? ArtifactStorage.Image(path.Target) : ArtifactImage.Absent;
        if (current != Preimage) throw new ArtifactConflictException("Source changed before commit");
        if (Preimage == Postimage) { File.Delete(CandidatePath); committed = true; return; }
        validateDestination?.Invoke();
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
