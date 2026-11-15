using Next.Domain;
using Next.Formats;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class ZipBoundaryTests
{
    public static void IndependentDeflateAndZip64(string root, bool zip64 = false)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "codec", "zip-boundary-oracle.json")));
        foreach (var row in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("id").GetString()!.StartsWith("zip64", StringComparison.Ordinal) != zip64 && !row.GetProperty("accepted").GetBoolean()) continue;
            var bytes = row.GetProperty("sourceBase64").GetBytesFromBase64();
            if (row.GetProperty("accepted").GetBoolean()) ZipArchiveValidation.Validate(bytes);
            else if (row.GetProperty("error").GetString() == "InvalidData") Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(bytes));
            else BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => ZipArchiveValidation.Validate(bytes));
        }
    }
    public static void RealEntryBoundaries()
    {
        foreach (var name in new[] { "../escape", "dir/../escape", "/absolute", "C:/absolute", "dir\\escape", "dir//file", "dir/./file", "bad\0name" })
            Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(Archive([name])));
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(Archive(["same", "same"])));
        ZipArchiveValidation.Validate(Archive(["data/你好.json", "data/file.json", "data/File.json"]));
        var large = Archive(["data/file"], new byte[2048]);
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => ZipArchiveValidation.Validate(large, new(MaxDecodedBytes: 1024)));
        var valid = Archive(["data/a", "data/b"], new byte[600]);
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => ZipArchiveValidation.Validate(valid, new(MaxDecodedBytes: 1024)));
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(valid[..^1]));
        var mismatched = Archive(["data/a"]); Encoding.ASCII.GetBytes("../bad").CopyTo(mismatched, 30);
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(mismatched));
        var wrongCrc = Archive(["data/a"]); var central = FindCentral(wrongCrc); wrongCrc[central + 16] ^= 1;
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(wrongCrc));
        var linked = Archive(["data/a"]); central = FindCentral(linked); BinaryPrimitives.WriteUInt32LittleEndian(linked.AsSpan(central + 38), 0xa0000000);
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(linked));
        var invalidUtf8 = Archive(["data/a"]); central = FindCentral(invalidUtf8);
        invalidUtf8[central + 9] |= 8; invalidUtf8[central + 46] = 0xff;
        Reject<InvalidDataException>(() => ZipArchiveValidation.Validate(invalidUtf8));
        var unsupported = Archive(["data/a"]); central = FindCentral(unsupported); unsupported[central + 10] = 99;
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => ZipArchiveValidation.Validate(unsupported));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => ZipArchiveValidation.Validate(valid, new(MaxContainerEntries: 0)));
        var root = ScratchDirectory.CreateChild();
        try
        {
            var original = Archive(["data/valid.json"]); var path = Path.Combine(root, "pack.zip"); File.WriteAllBytes(path, original);
            using (var candidate = new ArtifactStorage(root, World).Stage(new(World, "pack.zip", ArtifactKind.Archive), ArtifactImage.Present(Hash256.Compute(original)), Archive(["../escape"])))
            {
                candidate.Flush(); Reject<InvalidDataException>(() => candidate.Verify(stream =>
                {
                    using var bytes = new MemoryStream(); stream.CopyTo(bytes); ZipArchiveValidation.Validate(bytes.ToArray());
                }));
                Reject<InvalidOperationException>(candidate.Commit);
            }
            Require(File.ReadAllBytes(path).SequenceEqual(original) && !File.Exists(Path.Combine(root, "escape")), "unsafe actual ZIP candidate cannot pass verify/commit or escape to host paths");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static int FindCentral(byte[] bytes)
    {
        for (var offset = 0; offset + 4 <= bytes.Length; offset++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) == 0x02014b50) return offset;
        throw new InvalidOperationException("Test fixture has no central directory");
    }
    private static byte[] Archive(string[] names, byte[]? content = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                using var stream = archive.CreateEntry(name).Open(); stream.Write(content ?? new byte[] { 1, 2, 3 });
            }
        return output.ToArray();
    }
}
