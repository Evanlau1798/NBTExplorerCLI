using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Next.Infrastructure;

public sealed class TemporaryQueryDirectory : IDisposable
{
    public string Path { get; }
    private bool disposed;
    public TemporaryQueryDirectory(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        var parent = SafeStateDirectory.Ensure(System.IO.Path.GetDirectoryName(Path)!);
        using var lease = GuardedArtifactPath.AcquireDirectory(parent, "");
        if (!CreateDirectoryW(Path, IntPtr.Zero)) throw new IOException("Cannot exclusively create a temporary query directory", new Win32Exception(Marshal.GetLastPInvokeError()));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
}
