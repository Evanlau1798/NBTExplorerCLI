using System.Buffers.Binary;
using System.IO.Compression;
using ICSharpCode.SharpZipLib.Checksum;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static partial class ZipArchiveValidation
{
    public static byte[] ReplaceEntries(byte[] source, IEnumerable<(string Entry, Hash256 Expected, byte[] Replacement)> changes, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(changes);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (source.LongLength > limits.MaxCompressedBytes) throw Limit(0);
        var snapshot = source.ToArray();
        using var archive = OpenRead(snapshot, limits); var members = CentralMembers(snapshot, limits);
        var requested = changes.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(item => item.Entry is null || item.Expected is null || item.Replacement is null)) throw new ArgumentException("Invalid or oversized ZIP replacements", nameof(changes));
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal); long decoded = 0;
        foreach (var change in requested)
        {
            var entry = archive.GetEntry(change.Entry) ?? throw new EditRejectedException(EditFailure.Precondition, "ZIP entry is missing");
            decoded = checked(decoded + change.Replacement.Length); if (decoded > limits.MaxDecodedBytes) throw Limit(0);
            if (entry.FullName.EndsWith('/') || !replacements.TryAdd(change.Entry, change.Replacement.ToArray())) throw new EditRejectedException(EditFailure.Precondition, "ZIP entry is duplicated or a directory");
            var before = new byte[checked((int)entry.Length)]; using var content = entry.Open(); content.ReadExactly(before);
            if (Hash256.Compute(before) != change.Expected) throw new EditRejectedException(EditFailure.Precondition, "ZIP entry preimage changed");
        }
        var end = snapshot.Length - 22;
        while (U32(snapshot, end) != 0x06054b50 || U16(snapshot, end + 20) != snapshot.Length - end - 22) end--;
        var centralStart = checked((int)U32(snapshot, end + 16)); var centralSize = checked((int)U32(snapshot, end + 12));
        using var output = new MemoryStream(); output.Write(snapshot.AsSpan(0, centralStart));
        var central = snapshot.AsSpan(centralStart, centralSize).ToArray(); var position = 0; var changed = false;
        foreach (var member in members)
        {
            var recordLength = 46 + U16(central, position + 28) + U16(central, position + 30) + U16(central, position + 32);
            if (replacements.TryGetValue(member.Name, out var replacement))
            {
                var old = new byte[checked((int)member.DecodedBytes)]; using (var content = archive.GetEntry(member.Name)!.Open()) content.ReadExactly(old);
                if (!replacement.AsSpan().SequenceEqual(old))
                {
                    changed = true; var local = checked((int)U32(central, position + 42)); var headerLength = member.DataOffset - local;
                    var header = snapshot.AsSpan(local, headerLength).ToArray();
                    byte[] payload;
                    if (member.Method == 0) payload = replacement;
                    else
                    {
                        using var compressed = new MemoryStream(); using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true)) deflate.Write(replacement);
                        payload = compressed.ToArray();
                    }
                    if (output.Length + header.Length + payload.Length + centralSize + snapshot.Length - centralStart - centralSize > limits.MaxCompressedBytes) throw Limit(output.Length);
                    var crc = new Crc32(); crc.Update(replacement); var checksum = checked((uint)crc.Value);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)(U16(header, 6) & ~8));
                    BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(position + 8), (ushort)(U16(central, position + 8) & ~8));
                    void Patch(int headerOffset, int centralOffset, uint value)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(headerOffset), value);
                        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(position + centralOffset), value);
                    }
                    Patch(14, 16, checksum); Patch(18, 20, checked((uint)payload.Length)); Patch(22, 24, checked((uint)replacement.Length));
                    BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(position + 42), checked((uint)output.Position));
                    output.Write(header); output.Write(payload);
                }
            }
            position += recordLength;
        }
        if (!changed) return snapshot;
        var newCentral = output.Position; output.Write(central);
        var tail = snapshot.AsSpan(centralStart + centralSize).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(end - centralStart - centralSize + 16), checked((uint)newCentral)); output.Write(tail);
        var result = output.ToArray(); Validate(result, limits); return result;
    }
}
