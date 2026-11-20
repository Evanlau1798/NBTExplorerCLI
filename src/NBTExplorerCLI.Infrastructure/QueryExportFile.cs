using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Infrastructure;

public static class QueryExportFile
{
    private static readonly Guid Namespace = new("a15f381c-8ee2-4304-884b-1dc795f3bb31");
    public static void EnsureNew(string file)
    {
        var full = Path.GetFullPath(file); var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(Path.GetDirectoryName(full)!, Namespace, artifact);
        if (File.Exists(full) || Directory.Exists(full)) throw new ArtifactConflictException("Export destination already exists");
    }
    public static void WriteNew(string file, string content)
        => WriteNew(file, content, null);

    public static void WriteNew(string file, string content, Action<string>? validateDestination)
    {
        var full = Path.GetFullPath(file); var root = Path.GetDirectoryName(full)!;
        var bytes = new UTF8Encoding(false, true).GetBytes(content);
        if (bytes.Length > ArtifactStorage.MaximumBytes) throw new ArtifactSizeLimitException();
        var artifact = new ArtifactAddress(Namespace, Path.GetFileName(full), ArtifactKind.Json);
        using var path = GuardedArtifactPath.Acquire(root, Namespace, artifact);
        validateDestination?.Invoke(path.Target);
        using var candidate = new ArtifactCandidate(path, artifact, ArtifactImage.Absent, bytes, new ArtifactFileIo());
        candidate.Flush(); candidate.Verify(_ => { });
        // shortcut: checking markers and renaming are separate operations; atomic exclusion needs external world writers to cooperate.
        candidate.Commit(validateDestination is null ? null : () => validateDestination(path.Target));
    }
}
