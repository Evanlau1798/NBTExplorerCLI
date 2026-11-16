using Next.Domain;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Next.Formats;

public static partial class ZipArchiveValidation
{
    public static ZipArchive OpenRead(byte[] source, DecodeLimits? limits = null)
    {
        Validate(source, limits);
        var legacy = CodePagesEncodingProvider.Instance.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        var stream = new MemoryStream(source, writable: false);
        try { return new(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: legacy); }
        catch { stream.Dispose(); throw; }
    }

    public static void Validate(byte[] source, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); limits ??= DecodeLimits.Default; limits.Validate();
        if (source.LongLength > limits.MaxCompressedBytes) throw Limit(0);
        var members = CentralMembers(source, limits);
        var legacy = CodePagesEncodingProvider.Instance.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        using var input = new MemoryStream(source, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: legacy);
        if (archive.Entries.Count != members.Count) throw Invalid();
        long total = 0; var buffer = new byte[8192]; var crc = new Crc32(); var inflater = new Inflater(noHeader: true);
        for (var index = 0; index < members.Count; index++)
        {
            var entry = archive.Entries[index]; var member = members[index];
            if (entry.FullName != member.Name || entry.Length != member.DecodedBytes || entry.CompressedLength != member.CompressedBytes) throw Invalid();
            if (entry.Length > limits.MaxDecodedBytes - total) throw Limit(index);
            if (member.Method == 8) ValidateDeflate(source, member, inflater, buffer, crc);
            crc.Reset(); long readTotal = 0;
            using var content = entry.Open();
            while (true)
            {
                var read = content.Read(buffer);
                if (read == 0) break;
                if (read > entry.Length - readTotal || read > limits.MaxDecodedBytes - total) throw Invalid();
                crc.Update(new ArraySegment<byte>(buffer, 0, read)); readTotal += read; total += read;
            }
            if (readTotal != entry.Length || (uint)crc.Value != entry.Crc32) throw Invalid();
        }
    }

    private static List<Member> CentralMembers(byte[] source, DecodeLimits limits)
    {
        var end = -1;
        for (var offset = source.Length - 22; offset >= Math.Max(0, source.Length - 65_557); offset--)
            if (U32(source, offset) == 0x06054b50 && U16(source, offset + 20) == source.Length - offset - 22) { end = offset; break; }
        if (end < 0) throw Invalid();
        if (end >= 20 && U32(source, end - 20) == 0x07064b50) throw Unsupported();
        if (U16(source, end + 4) != 0 || U16(source, end + 6) != 0) throw Unsupported();
        var count = U16(source, end + 10); var size = U32(source, end + 12); var start = U32(source, end + 16);
        if (count == ushort.MaxValue || size == uint.MaxValue || start == uint.MaxValue) throw Unsupported();
        if (count != U16(source, end + 8) || (long)start + size > end) throw Invalid();
        if (count > limits.MaxContainerEntries) throw Limit(end);
        var position = (int)start; var centralEnd = checked(position + (int)size);
        var members = new List<Member>(count); var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            if (centralEnd - position < 46 || U32(source, position) != 0x02014b50) throw Invalid();
            var flags = U16(source, position + 8); var method = U16(source, position + 10);
            if ((flags & 1) != 0 || method is not (0 or 8) || U16(source, position + 34) != 0) throw Unsupported();
            if ((U32(source, position + 38) >> 16 & 0xf000) == 0xa000) throw Invalid();
            var nameLength = U16(source, position + 28);
            var length = 46 + nameLength + U16(source, position + 30) + U16(source, position + 32);
            if (length > centralEnd - position) throw Invalid();
            var local = U32(source, position + 42);
            var compressed = U32(source, position + 20); var decoded = U32(source, position + 24);
            if (local == uint.MaxValue || compressed == uint.MaxValue || decoded == uint.MaxValue) throw Unsupported();
            CheckExtra(source, position + 46 + nameLength, U16(source, position + 30));
            if (local > start || start - local < 30 || U32(source, (int)local) != 0x04034b50) throw Invalid();
            var localLength = 30 + U16(source, (int)local + 26) + U16(source, (int)local + 28);
            if (localLength > start - local || U16(source, (int)local + 6) != flags || U16(source, (int)local + 8) != method
                || U16(source, (int)local + 26) != nameLength
                || !source.AsSpan((int)local + 30, nameLength).SequenceEqual(source.AsSpan(position + 46, nameLength))) throw Invalid();
            if (U32(source, (int)local + 18) == uint.MaxValue || U32(source, (int)local + 22) == uint.MaxValue) throw Unsupported();
            CheckExtra(source, (int)local + 30 + nameLength, U16(source, (int)local + 28));
            var dataOffset = (int)local + localLength;
            if (compressed > start - dataOffset) throw Invalid();
            var encoding = (flags & 0x800) != 0 ? new UTF8Encoding(false, true)
                : CodePagesEncodingProvider.Instance.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
            string name;
            try { name = encoding.GetString(source, position + 46, nameLength); } catch (DecoderFallbackException) { throw Invalid(); }
            var path = name.EndsWith('/') ? name[..^1] : name;
            if (path.Length == 0 || name.Contains('\\') || name.Contains(':') || name.Contains('\0') || name.StartsWith('/')
                || path.Split('/').Any(segment => segment is "" or "." or "..") || !unique.Add(name)) throw Invalid();
            members.Add(new(name, method, dataOffset, (int)compressed, decoded, U32(source, position + 16))); position += length;
        }
        if (position != centralEnd) throw Invalid();
        return members;
    }
    private static void CheckExtra(byte[] source, int offset, int length)
    {
        var end = offset + length;
        while (offset < end)
        {
            if (end - offset < 4) throw Invalid();
            var kind = U16(source, offset); var size = U16(source, offset + 2); offset += 4;
            if (size > end - offset) throw Invalid();
            if (kind == 1) throw Unsupported();
            offset += size;
        }
    }
    private static void ValidateDeflate(byte[] source, Member member, Inflater inflater, byte[] buffer, Crc32 crc)
    {
        inflater.Reset(); crc.Reset(); inflater.SetInput(source, member.DataOffset, member.CompressedBytes);
        long total = 0;
        while (!inflater.IsFinished)
        {
            int read;
            try { read = inflater.Inflate(buffer, 0, (int)Math.Min(buffer.Length, member.DecodedBytes - total + 1)); }
            catch (Exception error) when (error is SharpZipBaseException or IndexOutOfRangeException) { throw Invalid(); }
            if (read > member.DecodedBytes - total || read == 0 && !inflater.IsFinished) throw Invalid();
            total += read; crc.Update(new ArraySegment<byte>(buffer, 0, read));
        }
        if (inflater.RemainingInput != 0 || total != member.DecodedBytes || (uint)crc.Value != member.Crc) throw Invalid();
    }
    private sealed record Member(string Name, int Method, int DataOffset, int CompressedBytes, long DecodedBytes, uint Crc);
    private static uint U32(byte[] source, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset));
    private static ushort U16(byte[] source, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset));
    private static InvalidDataException Invalid() => new("Invalid, ambiguous or unsafe ZIP entry");
    private static CodecException Limit(long offset) => new(CodecError.LimitExceeded, offset, "ZIP resource limit exceeded");
    private static CodecException Unsupported() => new(CodecError.UnsupportedProfile, 0, "ZIP64, split, encrypted and non-deflate ZIP are not supported by this profile");
}
