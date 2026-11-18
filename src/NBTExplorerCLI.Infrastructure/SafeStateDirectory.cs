namespace NBTExplorerCLI.Infrastructure;

internal static class SafeStateDirectory
{
    internal static string Ensure(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); var drive = Path.GetPathRoot(full)!;
        if (drive.Length != 3 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':' || full.Count(character => character == Path.DirectorySeparatorChar) > 256)
            throw new PlatformNotSupportedException("State requires a bounded local NTFS path");
        Create(full); return full;
    }
    private static void Create(string path)
    {
        if (Directory.Exists(path)) { using var existing = GuardedArtifactPath.AcquireDirectory(path, ""); return; }
        var parent = Path.GetDirectoryName(path) ?? throw new IOException("Missing state parent");
        Create(parent);
        using var guardedParent = GuardedArtifactPath.AcquireDirectory(parent, "");
        Directory.CreateDirectory(path);
        using var created = GuardedArtifactPath.AcquireDirectory(path, "");
    }
}
