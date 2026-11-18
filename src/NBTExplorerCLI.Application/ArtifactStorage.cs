using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

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
    IPendingWorldWrite AcquirePendingWrite() => throw new NotSupportedException("Storage does not support durable world write ordering");
    byte[] Read(ArtifactAddress artifact);
    IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes);
    IArtifactCandidate StageDelete(ArtifactAddress artifact, ArtifactImage preimage)
        => throw new NotSupportedException("Deletion staging is not implemented");
    IArtifactCandidate StageTracked(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, bool delete)
        => delete ? StageDelete(artifact, preimage) : Stage(artifact, preimage, bytes);
    IReadOnlyList<CandidateCleanupProof> CollectInactive(Hash256 access, IReadOnlyList<CandidateCleanupProof> proofs) => proofs;
}

public sealed record PendingWorldWrite(Hash256 PlanHash, Hash256 WorldBinding, Hash256 StoreBinding, Hash256 Anchor)
{
    public const string FileName = ".nbtcli-pending-write";
}

public interface IPendingWorldWrite : IDisposable
{
    PendingWorldWrite? Current { get; }
    void Begin(PendingWorldWrite write);
    void Complete();
}

public sealed class PendingWorldWriteException(Hash256 planHash) : IOException("Recover the pending plan using its original state root before writing again")
{
    public Hash256 PlanHash { get; } = planHash;
}
