using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NBTExplorerCLI.Infrastructure;

internal class ArtifactFileIo
{
    public virtual void Write(FileStream output, ReadOnlyMemory<byte> bytes) => output.Write(bytes.Span);
    public virtual void Flush(FileStream output) => output.Flush(flushToDisk: true);
    public virtual void Delete(FileStream source)
    {
        var disposition = new FileDisposition { DeleteFile = 1 };
        if (!SetFileInformationByHandle(source.SafeFileHandle, 4, ref disposition, 1))
            throw new IOException("Cannot delete the checked artifact handle", new Win32Exception(Marshal.GetLastPInvokeError()));
    }
    public virtual void Replace(string candidate, string target, bool absent)
    {
        if (absent) File.Move(candidate, target);
        else File.Replace(candidate, target, null, ignoreMetadataErrors: false);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileDisposition { public byte DeleteFile; }
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, int size);
}
