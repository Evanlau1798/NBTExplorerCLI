using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.IO.Compression;
using System.Text.Json;
using static NBTExplorerCLI.Verification.BinaryNbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class CompressionTests
{
    public static void IndependentOracle(string root)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "codec", "compression-oracle.json")));
        var expected = oracle.RootElement.GetProperty("rawBase64").GetBytesFromBase64();
        foreach (var row in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            var source = row.GetProperty("sourceBase64").GetBytesFromBase64();
            var codec = (NbtCompression)row.GetProperty("codec").GetByte();
            if (!row.GetProperty("accepted").GetBoolean())
            {
                var error = Enum.Parse<CodecError>(row.GetProperty("error").GetString()!);
                RejectCodec(error, () => NbtCompressionCodec.Decode(source, codec)); continue;
            }
            var result = NbtCompressionCodec.Decode(source, codec);
            var decoded = row.TryGetProperty("decodedBase64", out var value) ? value.GetBytesFromBase64() : expected;
            Require(result.Bytes.SequenceEqual(decoded) && result.ConsumedBytes == source.Length && result.Members == row.GetProperty("members").GetInt32(), "獨立Python framing/complete/input consumption需一致");
            if (decoded.Length != 0) Require(BinaryNbt.ReadDocument(result.Bytes).Document == BinaryNbtTests.Expected, "壓縮不能改NBT tags/bits");
        }
    }
    public static void WriterAndLimits(string root)
    {
        var raw = Golden(root);
        foreach (var codec in new[] { NbtCompression.Raw, NbtCompression.GZip, NbtCompression.Zlib })
        {
            var encoded = NbtCompressionCodec.Encode(raw, codec);
            Require(NbtCompressionCodec.Decode(encoded, codec).Bytes.SequenceEqual(raw), "writer需有完整framing");
            using var input = new MemoryStream(encoded);
            using Stream independent = codec switch { NbtCompression.GZip => new GZipStream(input, CompressionMode.Decompress), NbtCompression.Zlib => new ZLibStream(input, CompressionMode.Decompress), _ => input };
            using var output = new MemoryStream(); independent.CopyTo(output);
            Require(output.ToArray().SequenceEqual(raw), "writer內容須通過獨立BCL decoder");
            var exact = new DecodeLimits(MaxDecodedBytes: raw.Length, MaxCompressedBytes: encoded.Length);
            Require(NbtCompressionCodec.Decode(encoded, codec, exact).Bytes.SequenceEqual(raw), "exact byte quotas接受");
            RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Decode(encoded, codec, exact with { MaxDecodedBytes = raw.Length - 1 }));
            RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Decode(encoded, codec, exact with { MaxCompressedBytes = encoded.Length - 1 }));
            RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Encode(raw, codec, exact with { MaxCompressedBytes = 1 }));
        }
        RejectCodec(CodecError.UnsupportedProfile, () => NbtCompressionCodec.Decode([], NbtCompression.Lz4));
        Require(CompressionCapability.For(4).Read == SupportLevel.Unsupported && CompressionCapability.For(99).Write == SupportLevel.Unsupported
            && CompressionCapability.For(2, external: true).Read == SupportLevel.Unsupported, "未驗LZ4/external/custom不能當空資料");
    }
    public static void PrefixesAndShortStreams(string root)
    {
        var raw = Golden(root);
        foreach (var codec in new[] { NbtCompression.GZip, NbtCompression.Zlib })
        {
            var encoded = NbtCompressionCodec.Encode(raw, codec);
            for (var length = 0; length < encoded.Length; length++) RejectCodec(CodecError.CorruptCompression, () => NbtCompressionCodec.Decode(encoded[..length], codec));
            using var stream = new ShortStream(encoded);
            Require(NbtCompressionCodec.Decode(stream, codec).Bytes.SequenceEqual(raw) && stream.CanRead, "短讀非seek stream需完整且不關閉caller");
        }
    }
    public static void CompositionAndAdversarial(string root)
    {
        var raw = Golden(root);
        foreach (var codec in new[] { NbtCompression.Raw, NbtCompression.GZip, NbtCompression.Zlib })
        {
            var encoded = CompressedNbt.WriteDocument(BinaryNbtTests.Expected, codec);
            Require(CompressedNbt.ReadDocument(encoded, codec).Document == BinaryNbtTests.Expected, "document API must use complete frame validation");
            using var input = new ShortStream(encoded);
            Require(CompressedNbt.ReadDocument(input, codec).Document == BinaryNbtTests.Expected && input.CanRead, "document stream API has same semantics/ownership");
            var junk = NbtCompressionCodec.Encode([.. raw, 0], codec);
            RejectCodec(CodecError.TrailingData, () => CompressedNbt.ReadDocument(junk, codec));
        }
        foreach (var codec in new[] { NbtCompression.GZip, NbtCompression.Zlib })
        {
            var bomb = NbtCompressionCodec.Encode(new byte[1_000_000], codec);
            RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Decode(bomb, codec, new(MaxDecodedBytes: 1024)));
            var valid = NbtCompressionCodec.Encode(raw, codec);
            var random = new Random(73021);
            for (var trial = 0; trial < 1000; trial++)
            {
                var bytes = trial < 500 ? new byte[random.Next(256)] : valid.ToArray();
                if (trial < 500) random.NextBytes(bytes); else bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                try { _ = NbtCompressionCodec.Decode(bytes, codec, new(MaxDecodedBytes: 16_384)); }
                catch (CodecException error) { Require(error.Offset >= 0 && error.Offset <= bytes.Length, "diagnostic offsets stay inside source"); }
                catch (Exception error) { throw new InvalidOperationException($"{codec} trial {trial}, base64={Convert.ToBase64String(bytes)}: {error}"); }
            }
        }
        var member = NbtCompressionCodec.Encode(raw, NbtCompression.GZip);
        RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Decode([.. member, .. member], NbtCompression.GZip, new(MaxDecodedBytes: raw.Length)));
    }
    public static void EmptyWriterFrames(string root)
    {
        foreach (var codec in new[] { NbtCompression.GZip, NbtCompression.Zlib })
        {
            var encoded = NbtCompressionCodec.Encode([], codec);
            Require(encoded.Length > 0, "empty compressed writer must emit a complete frame");
            var decoded = NbtCompressionCodec.Decode(encoded, codec);
            Require(decoded.Bytes.Length == 0 && decoded.Members == 1 && decoded.ConsumedBytes == encoded.Length, "empty frame roundtrip completes");
            var exact = new DecodeLimits(MaxCompressedBytes: encoded.Length);
            Require(NbtCompressionCodec.Encode([], codec, exact).Length == encoded.Length, "empty frame exact compressed quota accepts");
            RejectCodec(CodecError.LimitExceeded, () => NbtCompressionCodec.Encode([], codec, exact with { MaxCompressedBytes = encoded.Length - 1 }));
            using var source = new MemoryStream(encoded);
            using Stream independent = codec == NbtCompression.GZip ? new GZipStream(source, CompressionMode.Decompress) : new ZLibStream(source, CompressionMode.Decompress);
            Require(independent.ReadByte() == -1, "independent BCL accepts valid empty frame");
        }
    }
    public static void EmptyMemberResourceBound(string root)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "codec", "compression-oracle.json")));
        var member = oracle.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "gzip-empty-member").GetProperty("sourceBase64").GetBytesFromBase64();
        var input = new byte[member.Length * 10_000];
        for (var index = 0; index < input.Length; index += member.Length) member.CopyTo(input, index);
        _ = NbtCompressionCodec.Decode(member, NbtCompression.GZip);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var result = NbtCompressionCodec.Decode(input, NbtCompression.GZip);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Require(result.Bytes.Length == 0 && result.Members == 10_000 && result.ConsumedBytes == input.Length, "empty member sequence retains complete framing/member count");
        Require(allocated < 8 * 1024 * 1024, $"200KB empty members must reuse decoder workspace; allocated={allocated}");
    }
    private sealed class ShortStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
    }
}
