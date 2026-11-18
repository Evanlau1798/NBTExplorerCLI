using System.Text;
using Next.Application;
using Next.Domain;

namespace Next.Infrastructure;

public static class QueryExportFile
{
    private static readonly Guid Namespace = new("a15f381c-8ee2-4304-884b-1dc795f3bb31");
    public static void EnsureNew(string file)
    {
        var full = Path.GetFullPath(file); var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(Path.GetDirectoryName(full)!, Namespace, artifact);
        if (File.Exists(full) || Directory.Exists(full)) throw new ArtifactConflictException("Export destination already exists");
    }
    public static void WriteNew(string file, string content, bool acceptIdentical = false)
    {
        var full = Path.GetFullPath(file); var root = Path.GetDirectoryName(full)!;
        var bytes = new UTF8Encoding(false, true).GetBytes(content);
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArtifactSizeLimitException();
        var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(root, Namespace, artifact);
        if (acceptIdentical && MatchesExisting(path.Target, bytes)) return;
        try
        {
            using var candidate = new ArtifactCandidate(path, artifact, ArtifactImage.Absent, bytes, new ArtifactFileIo());
            candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
        }
        catch (ArtifactConflictException) when (acceptIdentical && MatchesExisting(path.Target, bytes)) { }
    }
    private static bool MatchesExisting(string target, byte[] expected)
    {
        if (Directory.Exists(target)) throw new ArtifactConflictException("Export destination already exists");
        var current = ArtifactStorage.Image(target);
        if (current.ExpectedAbsent) return false;
        if (current != ArtifactImage.Present(Hash256.Compute(expected))) throw new ArtifactConflictException("Existing export content differs");
        return true;
    }
}
