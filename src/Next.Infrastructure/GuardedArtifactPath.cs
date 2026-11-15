using Microsoft.Win32.SafeHandles;
using Next.Domain;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Next.Infrastructure;

internal sealed class GuardedArtifactPath : IDisposable
{
    private readonly List<SafeFileHandle> directories = [];
    private IDisposable? writer;
    public string Target { get; }
    private GuardedArtifactPath(string target) => Target = target;

    public static GuardedArtifactPath Acquire(string root, Guid world, ArtifactAddress artifact)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Artifact writes require validated Windows NTFS");
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.WorldId != world || artifact.ContainerEntry is not null || artifact.RelativePath.Split('/').Any(part => part.StartsWith(".nbtcli-", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Requires a nonreserved physical artifact for the bound world", nameof(artifact));
        var full = Path.GetFullPath(root); var drive = Path.GetPathRoot(full)!;
        if (drive.Length != 3 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':')
            throw new PlatformNotSupportedException("Only local NTFS roots are validated");
        var volume = new DriveInfo(drive);
        var type = volume.DriveType;
        var format = type is DriveType.Fixed or DriveType.Removable ? volume.DriveFormat : "";
        if (!SupportsVolume(type, format)) throw new PlatformNotSupportedException("Only local NTFS roots are validated");
        foreach (var part in artifact.RelativePath.Split('/'))
        {
            var name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(name[3]))
                throw new ArgumentException("Reserved Windows artifact name", nameof(artifact));
        }
        var target = Path.GetFullPath(Path.Combine(full, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Artifact escapes the bound root", nameof(artifact));
        var lease = new GuardedArtifactPath(target);
        try
        {
            var parent = Path.GetDirectoryName(target)!;
            var current = drive;
            lease.HoldDirectory(current);
            foreach (var segment in parent[drive.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment); lease.HoldDirectory(current);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    internal void HoldWriter(ArtifactWriterLock shared) => writer = shared.Acquire();
    internal static bool SupportsVolume(DriveType type, string format) => type is DriveType.Fixed or DriveType.Removable && format == "NTFS";

    private void HoldDirectory(string path)
    {
        var handle = OpenNative(path, 0x80, FileShare.Read, 3, 0x02200000);
        try
        {
            var attributes = File.GetAttributes(handle);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new IOException("Directory reparse points are not allowed");
            directories.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static FileStream OpenFile(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var handle = OpenNative(path, access == FileAccess.Read ? 0x80000000u : 0xc0000000u, share, (uint)mode, 0x00200000);
        try
        {
            if ((File.GetAttributes(handle) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Artifact reparse points/directories are not allowed");
            return new FileStream(handle, access);
        }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle OpenNative(string path, uint access, FileShare share, uint mode, uint flags)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, mode, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError(); handle.Dispose();
        if (error is 2 or 3) throw new FileNotFoundException("Artifact path does not exist", path);
        throw new IOException("Cannot safely open artifact path", new Win32Exception(error));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, uint mode, uint flags, IntPtr template);

    public void Dispose()
    {
        writer?.Dispose(); writer = null;
        for (var index = directories.Count - 1; index >= 0; index--) directories[index].Dispose();
        directories.Clear();
    }
}
