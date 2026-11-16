using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class ZipEditsTests
{
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
    private static byte[] Archive(bool descriptor, bool stored)
    {
        using var stream = new MemoryStream(); var wrapper = descriptor ? new ForwardOnly(stream) : null;
        using (var archive = new ZipArchive(wrapper ?? (Stream)stream, ZipArchiveMode.Create, true, CodePagesEncodingProvider.Instance.GetEncoding(437)))
        {
            archive.Comment = "archive comment";
            foreach (var name in new[] { "café.txt", "protected.bin" })
            {
                var entry = archive.CreateEntry(name, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2020, 3, 4, 5, 6, 8, TimeSpan.Zero); entry.Comment = "entry comment"; entry.ExternalAttributes = 32;
                using var output = entry.Open(); output.Write("Original"u8);
            }
        }
        return stream.ToArray();
    }
    public static void ExactEntryRecordsAndIndependentInputs(string fixtureRoot)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "codec", "zip-boundary-oracle.json")));
        var inputs = oracle.RootElement.GetProperty("cases").EnumerateArray().Where(row => row.GetProperty("accepted").GetBoolean()).Select(row => row.GetProperty("sourceBase64").GetBytesFromBase64())
            .Concat(from descriptor in new[] { false, true } from stored in new[] { false, true } select Archive(descriptor, stored));
        foreach (var bytes in inputs)
        {
            using var original = ZipArchiveValidation.OpenRead(bytes); var entry = original.Entries[0];
            var before = new byte[checked((int)entry.Length)]; using (var stream = entry.Open()) stream.ReadExactly(before);
            Require(ZipArchiveValidation.ReplaceEntries(bytes, [(entry.FullName, Hash256.Compute(before), before)]).SequenceEqual(bytes), "no-op ZIP replacement retains every original byte");
            var output = ZipArchiveValidation.ReplaceEntries(bytes, [(entry.FullName, Hash256.Compute(before), "Changed雪"u8.ToArray())]);
            using var changed = ZipArchiveValidation.OpenRead(output); var actual = changed.GetEntry(entry.FullName)!;
            var end = bytes.Length - 22;
            while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end)) != 0x06054b50 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end + 20)) != bytes.Length - end - 22) end--;
            var localEnd = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16)));
            Require(output.AsSpan(0, localEnd).SequenceEqual(bytes.AsSpan(0, localEnd)) && changed.Comment == original.Comment
                && actual.Comment == entry.Comment && actual.LastWriteTime == entry.LastWriteTime && actual.ExternalAttributes == entry.ExternalAttributes, "selected/unselected local records and archive/entry metadata survive stored, deflate, CP437 and data-descriptor inputs");
            using (var stream = actual.Open()) { using var content = new MemoryStream(); stream.CopyTo(content); Require(content.ToArray().SequenceEqual("Changed雪"u8.ToArray()), "changed ZIP entry decompresses to the exact authorized bytes"); }
            foreach (var other in original.Entries.Skip(1))
            {
                using var oldStream = other.Open(); using var newStream = changed.GetEntry(other.FullName)!.Open(); using var oldBytes = new MemoryStream(); using var newBytes = new MemoryStream();
                oldStream.CopyTo(oldBytes); newStream.CopyTo(newBytes); Require(oldBytes.ToArray().SequenceEqual(newBytes.ToArray()), "unselected entry payload stays exact");
            }
        }
    }
    public static void RejectsUnboundOrOversizedChanges()
    {
        var bytes = Archive(false, false); var original = bytes.ToArray(); var hash = Hash256.Compute("Original"u8);
        Reject<EditRejectedException>(() => ZipArchiveValidation.ReplaceEntries(bytes, [("missing", hash, [1])]));
        Reject<EditRejectedException>(() => ZipArchiveValidation.ReplaceEntries(bytes, [("café.txt", Hash256.Compute([0]), [1])]));
        Reject<EditRejectedException>(() => ZipArchiveValidation.ReplaceEntries(bytes, [("café.txt", hash, [1]), ("café.txt", hash, [2])]));
        Require(ZipArchiveValidation.ReplaceEntries(bytes, [("café.txt", hash, "short"u8.ToArray())], new(MaxDecodedBytes: 32)).Length > 0, "small decoded budget has a reachable positive control");
        Reject<CodecException>(() => ZipArchiveValidation.ReplaceEntries(bytes, [("café.txt", hash, new byte[33])], new(MaxDecodedBytes: 32)));
        Require(bytes.SequenceEqual(original), "all accepted/rejected replacement requests leave input buffers immutable");
    }
    public static void AdditionsPreserveRecordsAndEnforceAbsence()
    {
        foreach (var descriptor in new[] { false, true })
        foreach (var stored in new[] { false, true })
        {
            var bytes = Archive(descriptor, stored); var original = bytes.ToArray();
            Require(ZipArchiveValidation.AddEntries(bytes, []).SequenceEqual(original), "empty additions preserve all original bytes");
            const string name = "new/雪.json";
            var result = ZipArchiveValidation.AddEntries(bytes, [(name, "Added雪"u8.ToArray())]);
            using var zip = ZipArchiveValidation.OpenRead(result); using var before = ZipArchiveValidation.OpenRead(bytes);
            var end = bytes.Length - 22;
            while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end)) != 0x06054b50 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end + 20)) != bytes.Length - end - 22) end--;
            var central = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16)));
            Require(result.AsSpan(0, central).SequenceEqual(bytes.AsSpan(0, central)) && zip.Comment == before.Comment && zip.Entries.Count == before.Entries.Count + 1, "entry addition preserves every original local record and archive comment");
            foreach (var entry in before.Entries)
            {
                var actual = zip.GetEntry(entry.FullName)!;
                Require(actual.Comment == entry.Comment && actual.LastWriteTime == entry.LastWriteTime && actual.ExternalAttributes == entry.ExternalAttributes, "unselected CP437/descriptors/stored/deflate metadata remains intact");
                using var value = actual.Open(); using var data = new MemoryStream(); value.CopyTo(data); Require(data.ToArray().SequenceEqual("Original"u8.ToArray()), "old entry payload remains exact");
            }
            using (var value = zip.GetEntry(name)!.Open()) { using var data = new MemoryStream(); value.CopyTo(data); Require(data.ToArray().SequenceEqual("Added雪"u8.ToArray()), "UTF8 added entry matches declared bytes"); }
            Reject<EditRejectedException>(() => ZipArchiveValidation.AddEntries(bytes, [("café.txt", [1])]));
            Reject<EditRejectedException>(() => ZipArchiveValidation.AddEntries(bytes, [(name, [1]), (name, [2])]));
            foreach (var invalid in new[] { "../escape", "a:b", "folder/", "/root" }) Reject<ArgumentException>(() => ZipArchiveValidation.AddEntries(bytes, [(invalid, [1])]));
            Require(ZipArchiveValidation.AddEntries(bytes, [("new.txt", [1])], new(MaxDecodedBytes: 32)).Length > 0, "small decoded budget has a reachable addition control");
            Reject<CodecException>(() => ZipArchiveValidation.AddEntries(bytes, [("new.txt", new byte[33])], new(MaxDecodedBytes: 32)));
            Reject<CodecException>(() => ZipArchiveValidation.AddEntries(bytes, [("new.txt", [1])], new(MaxCompressedBytes: bytes.Length)));
            Reject<CodecException>(() => ZipArchiveValidation.AddEntries(bytes, [("new.txt", [1])], new(MaxContainerEntries: 2)));
            Reject<CodecException>(() => ZipArchiveValidation.AddEntries(bytes, [(new string('x', 65_536), [1])]));
            Reject<CodecException>(() => ZipArchiveValidation.AddEntries(bytes, [(new string('雪', 32_768), [1])]));
            Require(bytes.SequenceEqual(original), "addition and rejection paths preserve the original buffer");
        }
    }
}
