using Next.Domain;
using System.Buffers.Binary;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;

namespace Next.Formats;

public static partial class ZipArchiveValidation
{
    public static byte[] AddEntries(byte[] source, IEnumerable<(string Entry, byte[] Contents)> additions, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(additions);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (source.LongLength > limits.MaxCompressedBytes) throw Limit(0);
        var snapshot = source.ToArray(); using var archive = OpenRead(snapshot, limits); var members = CentralMembers(snapshot, limits);
        var requested = additions.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(item => item.Entry is null || item.Contents is null)) throw new ArgumentException("Invalid or oversized ZIP additions", nameof(additions));
        if (requested.Length + members.Count >= ushort.MaxValue || requested.Length + members.Count > limits.MaxContainerEntries) throw Limit(0);
        var names = members.Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var entries = new List<(byte[] Name, byte[] Contents)>(); long length = snapshot.Length; long decoded = members.Sum(member => member.DecodedBytes);
        foreach (var item in requested)
        {
            if (item.Entry.Length > ushort.MaxValue) throw Limit(0);
            ArtifactAddress.ValidatePath(item.Entry, entry: true);
            if (item.Entry.Contains(':')) throw new ArgumentException("Invalid ZIP entry name", nameof(additions));
            if (!names.Add(item.Entry)) throw new EditRejectedException(EditFailure.Precondition, "Added ZIP entry must be absent and unique");
            var encoding = new UTF8Encoding(false, true); var nameLength = encoding.GetByteCount(item.Entry);
            if (nameLength > ushort.MaxValue) throw Limit(0);
            var name = encoding.GetBytes(item.Entry);
            decoded = checked(decoded + item.Contents.Length); length = checked(length + 76 + name.Length * 2L + item.Contents.Length);
            if (decoded > limits.MaxDecodedBytes || length > limits.MaxCompressedBytes) throw Limit(0);
            entries.Add((name, item.Contents.ToArray()));
        }
        if (entries.Count == 0) return snapshot;
        var end = snapshot.Length - 22;
        while (U32(snapshot, end) != 0x06054b50 || U16(snapshot, end + 20) != snapshot.Length - end - 22) end--;
        var centralStart = checked((int)U32(snapshot, end + 16)); var centralSize = checked((int)U32(snapshot, end + 12));
        using var output = new MemoryStream(checked((int)length)); output.Write(snapshot.AsSpan(0, centralStart));
        var records = new List<byte[]>(); var crc = new Crc32();
        foreach (var item in entries)
        {
            crc.Reset(); crc.Update(item.Contents); var checksum = checked((uint)crc.Value); var offset = checked((uint)output.Position);
            var local = new byte[30]; Set32(local, 0, 0x04034b50); Set16(local, 4, 20); Set16(local, 6, 0x800); Set16(local, 12, 33);
            Set32(local, 14, checksum); Set32(local, 18, checked((uint)item.Contents.Length)); Set32(local, 22, checked((uint)item.Contents.Length)); Set16(local, 26, checked((ushort)item.Name.Length));
            output.Write(local); output.Write(item.Name); output.Write(item.Contents);
            var central = new byte[46 + item.Name.Length]; Set32(central, 0, 0x02014b50); Set16(central, 4, 20); Set16(central, 6, 20); Set16(central, 8, 0x800); Set16(central, 14, 33);
            Set32(central, 16, checksum); Set32(central, 20, checked((uint)item.Contents.Length)); Set32(central, 24, checked((uint)item.Contents.Length));
            Set16(central, 28, checked((ushort)item.Name.Length)); Set32(central, 42, offset); item.Name.CopyTo(central.AsSpan(46)); records.Add(central);
        }
        var newStart = checked((uint)output.Position); output.Write(snapshot.AsSpan(centralStart, centralSize));
        foreach (var record in records) output.Write(record);
        var newSize = checked((uint)output.Position - newStart);
        var tail = snapshot.AsSpan(centralStart + centralSize).ToArray(); var endOffset = end - centralStart - centralSize;
        Set16(tail, endOffset + 8, checked((ushort)names.Count)); Set16(tail, endOffset + 10, checked((ushort)names.Count));
        Set32(tail, endOffset + 12, newSize); Set32(tail, endOffset + 16, newStart); output.Write(tail);
        var result = output.ToArray(); Validate(result, limits); return result;
    }
    private static void Set16(byte[] buffer, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);
    private static void Set32(byte[] buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
}
