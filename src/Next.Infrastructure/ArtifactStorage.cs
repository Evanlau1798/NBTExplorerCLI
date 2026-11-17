using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

public sealed class ArtifactSizeLimitException() : IOException("Artifact exceeds 64 MiB limit");

public sealed class ArtifactStorage : IArtifactStorage
{
    public Hash256 AccessBinding => GuardedArtifactPath.AccessBinding(root, world);
    internal const int MaximumBytes = 67_108_864;
    private readonly string root;
    private readonly Guid world;
    private readonly ArtifactFileIo io;
    private readonly ArtifactWriterLock writer;
    public ArtifactStorage(string root, Guid worldId) : this(root, worldId, new ArtifactFileIo()) { }
    internal ArtifactStorage(string root, Guid worldId, ArtifactFileIo io)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (worldId == Guid.Empty) throw new ArgumentException("World identity is required", nameof(worldId));
        (this.root, world, this.io) = (Path.GetFullPath(root), worldId, io);
        writer = new(this.root);
    }
    public byte[] Read(ArtifactAddress artifact)
    {
        using var path = GuardedArtifactPath.Acquire(root, world, artifact);
        return ReadFile(path.Target);
    }
    public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
        => StageCore(artifact, preimage, bytes, delete: false);
    public IArtifactCandidate StageDelete(ArtifactAddress artifact, ArtifactImage preimage)
        => StageCore(artifact, preimage, ReadOnlyMemory<byte>.Empty, delete: true);
    private IArtifactCandidate StageCore(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes, bool delete)
    {
        ArgumentNullException.ThrowIfNull(preimage);
        if (bytes.Length > MaximumBytes) throw new ArtifactSizeLimitException();
        var path = GuardedArtifactPath.Acquire(root, world, artifact, allowMissingParents: preimage.ExpectedAbsent);
        try { path.HoldWriter(writer); return new ArtifactCandidate(path, preimage, bytes, io, delete); }
        catch { path.Dispose(); throw; }
    }
    internal static byte[] ReadFile(string path)
    {
        using var stream = GuardedArtifactPath.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new ArtifactSizeLimitException();
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return bytes;
    }
    internal static ArtifactImage Image(string path)
    {
        try { return ArtifactImage.Present(Hash256.Compute(ReadFile(path))); }
        catch (FileNotFoundException) { return ArtifactImage.Absent; }
    }
    internal static ArtifactImage Image(Stream stream)
    {
        if (stream.Length > MaximumBytes) throw new IOException("Artifact exceeds 64 MiB limit");
        return ArtifactImage.Present(new Hash256(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))));
    }
}
