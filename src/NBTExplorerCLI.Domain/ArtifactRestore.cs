using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public sealed record ArtifactImage
{
    public Hash256? Hash { get; }
    public bool ExpectedAbsent => Hash is null;
    private ArtifactImage(Hash256? hash) => Hash = hash;
    public static ArtifactImage Absent { get; } = new((Hash256?)null);
    public static ArtifactImage Present(Hash256 hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return new(hash);
    }
}

public sealed record BackupArtifact(ArtifactAddress Artifact, ArtifactImage Image);
public sealed class ArtifactRestore
{
    public Hash256 VerifiedManifestHash { get; }
    public ImmutableArray<BackupArtifact> AuthorizedArtifacts { get; }
    public ImmutableDictionary<ArtifactAddress, ArtifactImage> CurrentImages { get; }
    public ArtifactRestore(Hash256 verifiedManifestHash, IEnumerable<BackupArtifact> authorizedArtifacts,
        IEnumerable<KeyValuePair<ArtifactAddress, ArtifactImage>> currentImages)
    {
        ArgumentNullException.ThrowIfNull(verifiedManifestHash);
        ArgumentNullException.ThrowIfNull(authorizedArtifacts);
        ArgumentNullException.ThrowIfNull(currentImages);
        AuthorizedArtifacts = authorizedArtifacts.Take(100_001).ToImmutableArray();
        var current = currentImages.Take(100_001).ToArray();
        if (AuthorizedArtifacts.Length is 0 or > 100_000 || AuthorizedArtifacts.Any(item => item is null || item.Artifact is null
            || item.Image is null || item.Artifact.ContainerEntry is not null)
            || AuthorizedArtifacts.Select(item => (item.Artifact.WorldId, item.Artifact.RelativePath)).Distinct().Count() != AuthorizedArtifacts.Length
            || AuthorizedArtifacts.Select(item => item.Artifact.WorldId).Distinct().Count() != 1
            || current.Length != AuthorizedArtifacts.Length || current.Any(pair => pair.Key is null || pair.Value is null))
            throw new ArgumentException("Restore requires a unique physical artifact set for one world");
        CurrentImages = current.ToImmutableDictionary();
        if (AuthorizedArtifacts.Any(item => !CurrentImages.ContainsKey(item.Artifact)))
            throw new ArgumentException("Current image set must exactly match the backup-authorized artifact set", nameof(currentImages));
        VerifiedManifestHash = verifiedManifestHash;
    }
    public bool VerifyPreimage(ArtifactAddress artifact, byte[]? bytes)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return CurrentImages.TryGetValue(artifact, out var expected) && Matches(expected, bytes);
    }
    public bool VerifyRestoredImage(ArtifactAddress artifact, byte[]? bytes)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var entry = AuthorizedArtifacts.SingleOrDefault(item => item.Artifact == artifact);
        return entry is not null && Matches(entry.Image, bytes);
    }
    private static bool Matches(ArtifactImage image, byte[]? bytes)
        => bytes is null ? image.ExpectedAbsent : image.Hash == Hash256.Compute(bytes);
}
