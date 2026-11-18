using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Buffers.Binary;
using System.Text.Json;
using static NBTExplorerCLI.Verification.BinaryNbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class RegionTests
{
    public static void IndependentOracle(string root)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "codec", "region-oracle.json")));
        var bytes = oracle.RootElement.GetProperty("sourceBase64").GetBytesFromBase64();
        var file = RegionFile.Read(bytes);
        foreach (var row in oracle.RootElement.GetProperty("chunks").EnumerateArray())
        {
            var index = row.GetProperty("index").GetInt32(); var chunk = file.Chunks[index];
            Require(chunk.SectorOffset == row.GetProperty("sector").GetInt32() && chunk.SectorCount == row.GetProperty("count").GetByte()
                && chunk.Timestamp == row.GetProperty("timestamp").GetUInt32() && chunk.CodecId == row.GetProperty("codec").GetByte(), "index agrees with independent struct oracle");
            Require(Hash256.Compute(file.CompressedPayload(index)).Hex == row.GetProperty("payloadSha256").GetString(), "opaque/known compressed bytes preserved");
            if (chunk.Capability.Read == SupportLevel.Supported) Require(file.ReadChunk(index).Document == Expected, "independent compressed NBT payload agrees");
        }
        Require(file.WithChunk(0, Expected, NbtCompression.Zlib, 0).SequenceEqual(bytes), "independent input no-op retains exact bytes");
    }
    public static void Boundaries(string root)
    {
        var valid = Sample(root);
        foreach (var length in new[] { 0, 4096, 8191, valid.Length - 1 })
            RejectCodec(CodecError.InvalidRegion, () => RegionFile.Read(valid[..length]));
        foreach (var location in new uint[] { 1, 0x100, 0x101, 0x200, 0x400, 0x401, 0xffffff01 })
        {
            var bad = valid.ToArray(); Put(bad, 0, location);
            RejectCodec(CodecError.InvalidRegion, () => RegionFile.Read(bad));
        }
        var overlap = valid.ToArray(); Put(overlap, 4, 0x201);
        RejectCodec(CodecError.InvalidRegion, () => RegionFile.Read(overlap));
        foreach (var length in new uint[] { 0, 4093, 0xffffffff })
        {
            var bad = valid.ToArray(); Put(bad, 8192, length);
            RejectCodec(CodecError.InvalidRegion, () => RegionFile.Read(bad));
        }
        var external = valid.ToArray(); external[8196] = 0x82;
        RejectCodec(CodecError.InvalidRegion, () => RegionFile.Read(external));
        var file = RegionFile.Read(valid);
        valid[8196] = 99;
        Require(file.Chunks[0].CodecId == 2, "immutable index/snapshot cannot alias caller bytes");
        var payload = file.CompressedPayload(0); payload[0] ^= 1;
        Require(file.ReadChunk(0).Document == Expected, "returned payload cannot mutate region");
        Require(file.Chunks.Count == 1024 && !file.Chunks[2].Present, "absent differs from unreadable");
    }

    public static void CandidatePreservesOthers(string root)
    {
        var original = Sample(root); var file = RegionFile.Read(original);
        Require(file.WithChunk(0, Expected, NbtCompression.Zlib, 999).SequenceEqual(original), "no-op bytes/timestamps/padding unchanged");
        var edited = new NbtDocument("edited", new NbtString(new string('x', 12_000)));
        var candidate = file.WithChunk(0, edited, NbtCompression.Raw, uint.MaxValue);
        var parsed = RegionFile.Read(candidate);
        Require(parsed.ReadChunk(0).Document == edited && parsed.Chunks[0].SectorCount == 3 && parsed.Chunks[0].Timestamp == uint.MaxValue, "growing chunk is valid with explicit timestamp");
        Require(parsed.CompressedPayload(1).SequenceEqual(file.CompressedPayload(1)) && parsed.Chunks[1] == file.Chunks[1], "unselected codec/compressed bytes/timestamp/location remain identical");
        Require(candidate.AsSpan(8192, original.Length - 8192).SequenceEqual(original.AsSpan(8192)), "append-only candidate preserves original sectors including padding");
        Require(file.ReadChunk(0).Document == Expected && original.SequenceEqual(Sample(root)), "candidate has no implicit commit");
        var added = RegionFile.Read(file.WithChunk(2, edited, NbtCompression.GZip, 0));
        Require(added.ReadChunk(2).Document == edited && added.Chunks[1] == file.Chunks[1], "absent selected chunk may be added without changing others");
    }

    public static void SectorWidthsAndUnsupported(string root)
    {
        var file = RegionFile.Read(Sample(root));
        var maximum = new NbtDocument("maxx", new NbtLongArray(new long[(255 * 4096 - 5 - 11) / 8]));
        Require(BinaryNbt.MeasureDocument(maximum).Bytes == 255 * 4096 - 5, "fixture exactly fills 255 sectors including length/flag");
        var accepted = RegionFile.Read(file.WithChunk(0, maximum, NbtCompression.Raw, 1));
        Require(accepted.Chunks[0].SectorCount == 255 && accepted.ReadChunk(0).Document == maximum, "255 sectors exact boundary accepts");
        var oversized = new NbtDocument("maxxx", maximum.Root);
        RejectCodec(CodecError.LimitExceeded, () => file.WithChunk(0, oversized, NbtCompression.Raw, 1));
        foreach (byte flag in new byte[] { 0, 4, 99, 0x82, 0xff })
        {
            var bytes = Sample(root); bytes[12292] = flag;
            if ((flag & 0x80) != 0) Put(bytes, 12288, 1);
            var opaque = RegionFile.Read(bytes);
            Require(opaque.Chunks[1].Present && opaque.Chunks[1].Capability.Read == SupportLevel.Unsupported, "opaque chunks remain present/unsupported");
            RejectCodec(CodecError.UnsupportedProfile, () => opaque.ReadChunk(1));
            RejectCodec(CodecError.UnsupportedProfile, () => opaque.WithChunk(1, Expected, NbtCompression.Zlib, 1));
            var changed = RegionFile.Read(opaque.WithChunk(0, new NbtDocument("", new NbtInt(7)), NbtCompression.Zlib, 2));
            Require(changed.Chunks[1] == opaque.Chunks[1] && changed.CompressedPayload(1).SequenceEqual(opaque.CompressedPayload(1)), "explicit single-chunk scope preserves opaque bytes without claiming semantic validation");
        }
    }

    public static void CorruptSelectedAndCapacity(string root)
    {
        var corrupted = Sample(root); corrupted[8197] ^= 1;
        var file = RegionFile.Read(corrupted);
        RejectCodec(CodecError.CorruptCompression, () => file.WithChunk(0, new NbtDocument("", new NbtInt(7)), NbtCompression.Raw, 1));
        var original = Sample(root); var valid = RegionFile.Read(original);
        RejectCodec(CodecError.LimitExceeded, () => valid.ReadChunk(0, new(MaxCompressedBytes: 1)));
        RejectCodec(CodecError.LimitExceeded, () => valid.WithChunk(0, Expected, NbtCompression.Zlib, 0, new(MaxDecodedBytes: 1)));
        var full = new byte[RegionFile.MaximumArtifactBytes]; original.CopyTo(full, 0);
        var atCapacity = RegionFile.Read(full);
        Require(atCapacity.WithChunk(0, Expected, NbtCompression.Zlib, 0).SequenceEqual(full), "at-capacity no-op does not allocate extra sectors");
        RejectCodec(CodecError.LimitExceeded, () => atCapacity.WithChunk(0, new NbtDocument("", new NbtInt(7)), NbtCompression.Zlib, 1));
    }

    internal static byte[] Sample(string root)
    {
        var result = new byte[4 * 4096]; Put(result, 0, 0x201); Put(result, 4, 0x301);
        Put(result, 4096, 123); Put(result, 4100, 456);
        var raw = Golden(root);
        foreach (var pair in new[] { (8192, NbtCompression.Zlib), (12288, NbtCompression.GZip) })
        {
            var payload = NbtCompressionCodec.Encode(raw, pair.Item2); Put(result, pair.Item1, (uint)payload.Length + 1);
            result[pair.Item1 + 4] = (byte)pair.Item2; payload.CopyTo(result, pair.Item1 + 5);
        }
        result[^1] = 0x55;
        return result;
    }
    internal static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
