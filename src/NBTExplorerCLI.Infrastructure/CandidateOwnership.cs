using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Infrastructure;

internal static class CandidateOwnership
{
    private static readonly ArtifactFileIo Deletion = new();

    internal static IReadOnlyList<CandidateCleanupProof> Collect(string root, Guid world, Hash256 access, IReadOnlyList<CandidateCleanupProof> proofs)
    {
        try
        {
            using var lease = GuardedArtifactPath.AcquireDirectory(root, "");
            if (GuardedArtifactPath.AccessBinding(root, world) != access) return proofs;
            // A separate lease excludes live candidates even when they share a storage instance.
            lease.HoldWriter(new ArtifactWriterLock(root));
            return proofs.Where(proof => !CollectOne(root, proof)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return proofs; }
    }

    private static bool CollectOne(string root, CandidateCleanupProof proof)
    {
        try
        {
            const string prefix = ".nbtcli-";
            const string suffix = ".candidate";
            if (!Path.IsPathFullyQualified(proof.Path)) return true;
            var relative = Path.GetRelativePath(root, proof.Path).Replace('\\', '/');
            ArtifactAddress.ValidatePath(relative, entry: false);
            var name = Path.GetFileName(relative);
            if (name.Length != prefix.Length + 32 + suffix.Length || !name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(suffix, StringComparison.Ordinal) || !Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out _)) return true;
            var separator = relative.LastIndexOf('/');
            using var branch = GuardedArtifactPath.AcquireDirectory(root, separator < 0 ? "" : relative[..separator]);
            using var file = GuardedArtifactPath.OpenFile(Path.Combine(branch.Target, name), FileMode.Open, FileAccess.Read, FileShare.None, deleteAccess: true);
            if (GuardedArtifactPath.FileIdentity(file) != proof.FileIdentity || ArtifactStorage.Image(file).Hash != proof.ImageHash)
                return false;
            Deletion.Delete(file);
            return true;
        }
        catch (FileNotFoundException) { return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
