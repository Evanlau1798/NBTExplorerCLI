using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

public sealed class ArtifactSizeLimitException : IOException
{
    public ArtifactSizeLimitException() : this(ArtifactStorage.MaximumBytes) { }
    internal ArtifactSizeLimitException(int maximumBytes) : base(maximumBytes == ArtifactStorage.MaximumBytes ? "Artifact exceeds 64 MiB limit" : $"Artifact exceeds {maximumBytes} byte limit") { }
}

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
        => ReadBounded(artifact, MaximumBytes);
    public byte[] ReadBounded(ArtifactAddress artifact, int maximumBytes)
    {
        if (maximumBytes is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var path = GuardedArtifactPath.Acquire(root, world, artifact);
        using var stream = GuardedArtifactPath.OpenFile(path.Target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return ReadStream(stream, maximumBytes);
    }
    internal ArtifactImage ReadImage(ArtifactAddress artifact)
    {
        try
        {
            using var path = GuardedArtifactPath.Acquire(root, world, artifact);
            using var stream = GuardedArtifactPath.OpenFile(path.Target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return Image(stream);
        }
        catch (FileNotFoundException) { return ArtifactImage.Absent; }
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
        return ReadStream(stream, MaximumBytes);
    }
    private static byte[] ReadStream(Stream stream, int maximumBytes)
    {
        if (stream.Length > maximumBytes) throw new ArtifactSizeLimitException(maximumBytes);
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
