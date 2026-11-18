using Microsoft.Win32.SafeHandles;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Buffers.Binary;

namespace NBTExplorerCLI.Infrastructure;

internal sealed class GuardedArtifactPath : IDisposable
{
    private readonly List<SafeFileHandle> directories = [];
    private HashSet<string>? branches;
    private IDisposable? writer;
    public string Target { get; }
    public string StagingDirectory { get; private set; }
    private GuardedArtifactPath(string target) => (Target, StagingDirectory) = (target, Path.GetDirectoryName(target)!);

    public static GuardedArtifactPath Acquire(string root, Guid world, ArtifactAddress artifact, bool allowMissingParents = false)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.WorldId != world || artifact.ContainerEntry is not null || artifact.RelativePath.Split('/').Any(part => part.StartsWith(".nbtcli-", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Requires a nonreserved physical artifact for the bound world", nameof(artifact));
        return AcquireCore(root, artifact.RelativePath, directory: false, allowMissingParents);
    }
    internal static GuardedArtifactPath AcquireDirectory(string root, string relativeDirectory)
    {
        if (relativeDirectory.Length != 0) ArtifactAddress.ValidatePath(relativeDirectory, entry: false);
        return AcquireCore(root, relativeDirectory, directory: true);
    }
    internal static GuardedArtifactPath AcquirePendingWrite(string root)
        => AcquireCore(root, PendingWorldWrite.FileName, directory: false);
    internal static Hash256 AccessBinding(string root, Guid world)
    {
        using var lease = AcquireDirectory(root, "");
        if (!GetFileInformationByHandleEx(lease.directories[^1], 18, out var identity, 24))
            throw new IOException("Cannot identify the bound NTFS root", new Win32Exception(Marshal.GetLastPInvokeError()));
        Span<byte> key = stackalloc byte[40];
        world.TryWriteBytes(key);
        BinaryPrimitives.WriteUInt64LittleEndian(key[16..], identity.VolumeSerialNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(key[24..], identity.FileIdLow);
        BinaryPrimitives.WriteUInt64LittleEndian(key[32..], identity.FileIdHigh);
        return Hash256.Compute([.. "nbtcli.ntfs-directory-v1\0"u8, .. key]);
    }
    internal static Hash256 FileIdentity(FileStream file)
    {
        if (!GetFileInformationByHandleEx(file.SafeFileHandle, 18, out var identity, 24))
            throw new IOException("Cannot identify the guarded NTFS file", new Win32Exception(Marshal.GetLastPInvokeError()));
        Span<byte> bytes = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, identity.VolumeSerialNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], identity.FileIdLow);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], identity.FileIdHigh);
        return Hash256.Compute(bytes);
    }
    private static GuardedArtifactPath AcquireCore(string root, string relativePath, bool directory, bool allowMissingParents = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Artifact access requires validated Windows NTFS");
        var full = Path.GetFullPath(root); var drive = Path.GetPathRoot(full)!;
        if (drive.Length != 3 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':')
            throw new PlatformNotSupportedException("Only local NTFS roots are validated");
        var volume = new DriveInfo(drive);
        var type = volume.DriveType;
        var format = type is DriveType.Fixed or DriveType.Removable ? volume.DriveFormat : "";
        if (!SupportsVolume(type, format)) throw new PlatformNotSupportedException("Only local NTFS roots are validated");
        foreach (var part in relativePath.Split('/'))
        {
            var name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(name[3]))
                throw new ArgumentException("Reserved Windows artifact name", nameof(relativePath));
        }
        var target = Path.GetFullPath(Path.Combine(full, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!(directory && target == full) && !target.StartsWith(Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Artifact escapes the bound root", nameof(relativePath));
        var lease = new GuardedArtifactPath(target);
        try
        {
            var parent = directory ? target : Path.GetDirectoryName(target)!;
            var current = drive;
            lease.HoldDirectory(current);
            lease.StagingDirectory = current;
            foreach (var segment in parent[drive.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                try { lease.HoldDirectory(current); }
                catch (FileNotFoundException) when (allowMissingParents && current.StartsWith(Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { break; }
                lease.StagingDirectory = current;
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    internal void HoldWriter(ArtifactWriterLock shared) => writer = shared.Acquire();
    internal void HoldExistingBranch(string ancestor, string relativeDirectory)
    {
        var current = Path.GetFullPath(ancestor);
        bool InitiallyHeld(string directory) => directory == StagingDirectory
            || StagingDirectory.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!InitiallyHeld(current) && branches?.Contains(current) != true)
            throw new ArgumentException("Branch must start at an already held ancestor", nameof(ancestor));
        ArtifactAddress.ValidatePath(relativeDirectory, entry: false);
        foreach (var segment in relativeDirectory.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (InitiallyHeld(current) || branches?.Contains(current) == true) continue;
            HoldDirectory(current); (branches ??= new(StringComparer.Ordinal)).Add(current);
        }
    }
    internal bool HoldParents(bool create)
    {
        var parent = Path.GetDirectoryName(Target)!;
        if (parent == StagingDirectory) return true;
        var current = StagingDirectory;
        foreach (var segment in Path.GetRelativePath(current, parent).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try { HoldDirectory(current); }
            catch (FileNotFoundException)
            {
                if (!create) return false;
                Directory.CreateDirectory(current); HoldDirectory(current);
            }
        }
        return true;
    }
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

    internal static FileStream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, bool deleteAccess = false)
    {
        var desired = (access == FileAccess.Read ? 0x80000000u : 0xc0000000u) | (deleteAccess ? 0x10000u : 0u);
        var handle = OpenNative(path, desired, share, (uint)mode, 0x00200000);
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
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileIdentity
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out NativeFileIdentity information, uint size);

    public void Dispose()
    {
        writer?.Dispose(); writer = null;
        for (var index = directories.Count - 1; index >= 0; index--) directories[index].Dispose();
        directories.Clear();
        branches?.Clear();
    }
}
