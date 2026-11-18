using System.Text;
using Next.Domain;

namespace Next.Infrastructure;

public static class QueryExportFile
{
    private static readonly Guid Namespace = new("a15f381c-8ee2-4304-884b-1dc795f3bb31");
    public static void EnsureNew(string file)
    {
        var full = Path.GetFullPath(file); var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(Path.GetDirectoryName(full)!, Namespace, artifact);
        if (File.Exists(full) || Directory.Exists(full)) throw new Next.Application.ArtifactConflictException("Export destination already exists");
    }
    public static void WriteNew(string file, string content)
    {
        var full = Path.GetFullPath(file); var root = Path.GetDirectoryName(full)!;
        var bytes = new UTF8Encoding(false, true).GetBytes(content);
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArtifactSizeLimitException();
        var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(root, Namespace, artifact);
        using var candidate = new ArtifactCandidate(path, artifact, ArtifactImage.Absent, bytes, new ArtifactFileIo());
        candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
    }
}
