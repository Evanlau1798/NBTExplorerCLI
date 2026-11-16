using Next.Domain;

namespace Next.Application;

public sealed class ArtifactConflictException(string message) : IOException(message);
public sealed class ArtifactCommitException(string message, Exception cause) : IOException(message, cause);

public interface IArtifactCandidate : IDisposable
{
    string CandidatePath { get; }
    ArtifactImage Preimage { get; }
    ArtifactImage Postimage { get; }
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
}
