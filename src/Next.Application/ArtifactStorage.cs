using Next.Domain;

namespace Next.Application;

public sealed class ArtifactConflictException(string message) : IOException(message);
public sealed class ArtifactCommitException(string message, Exception cause) : IOException(message, cause);
public sealed record CandidateCleanupProof(string Path, Hash256 FileIdentity, Hash256 ImageHash);

public interface IArtifactCandidate : IDisposable
{
    string CandidatePath { get; }
    ArtifactImage Preimage { get; }
    ArtifactImage Postimage { get; }
    CandidateCleanupProof? CleanupProof => null;
    void Flush();
    void Verify(Action<Stream> verify);
    void Commit();
}

public interface IArtifactStorage
{
    Hash256? AccessBinding => null;
    byte[] Read(ArtifactAddress artifact);
    IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes);
    IArtifactCandidate StageDelete(ArtifactAddress artifact, ArtifactImage preimage)
        => throw new NotSupportedException("Deletion staging is not implemented");
    IArtifactCandidate StageTracked(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, bool delete)
        => delete ? StageDelete(artifact, preimage) : Stage(artifact, preimage, bytes);
    IReadOnlyList<CandidateCleanupProof> CollectInactive(Hash256 access, IReadOnlyList<CandidateCleanupProof> proofs) => proofs;
}
