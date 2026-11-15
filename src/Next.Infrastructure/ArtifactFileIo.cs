namespace Next.Infrastructure;

internal class ArtifactFileIo
{
    public virtual void Write(FileStream output, ReadOnlyMemory<byte> bytes) => output.Write(bytes.Span);
    public virtual void Flush(FileStream output) => output.Flush(flushToDisk: true);
    public virtual void Replace(string candidate, string target, bool absent)
    {
        if (absent) File.Move(candidate, target);
        else File.Replace(candidate, target, null, ignoreMetadataErrors: false);
    }
}
