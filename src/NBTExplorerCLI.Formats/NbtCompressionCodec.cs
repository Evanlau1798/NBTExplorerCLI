using NBTExplorerCLI.Domain;
using System.Buffers.Binary;
using System.IO.Compression;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using ICSharpCode.SharpZipLib.GZip;

namespace NBTExplorerCLI.Formats;

public sealed record DecodedFrame(byte[] Bytes, int ConsumedBytes, int Members);
public static class NbtCompressionCodec
{
    public static DecodedFrame Decode(byte[] source, NbtCompression codec, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= DecodeLimits.Default; limits.Validate(); CheckCodec(codec);
        if (source.LongLength > limits.MaxCompressedBytes) throw Limit(source.Length);
        if (codec == NbtCompression.Raw)
        {
            if (source.LongLength > limits.MaxDecodedBytes) throw Limit(source.Length);
            return new(source.ToArray(), source.Length, 1);
        }
        using var output = new MemoryStream();
        var offset = 0; var members = 0;
        var inflater = new Inflater(codec == NbtCompression.GZip);
        var buffer = new byte[8192]; var crc = new Crc32();
        if (codec == NbtCompression.Zlib)
        {
            // SharpZipLib ignores CINFO; RFC 1950 only permits windows up to 32 KiB.
            if (source.Length < 2 || (source[0] >> 4) > 7) throw Corrupt(0);
            Inflate(source, ref offset, output, limits, inflater, buffer, crc);
            if (offset != source.Length) throw new CodecException(CodecError.TrailingData, offset, "Trailing zlib data");
            members = 1;
        }
        else
        {
            do
            {
                ReadGzipHeader(source, ref offset);
                var start = output.Length;
                var checksum = Inflate(source, ref offset, output, limits, inflater, buffer, crc);
                Need(source, offset, 8);
                if (BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset)) != checksum
                    || BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4)) != (uint)(output.Length - start))
                    throw Corrupt(offset);
                offset += 8; members++;
                if (offset < source.Length && (source[offset] != 0x1f || (source.Length - offset >= 2 && source[offset + 1] != 0x8b)))
                    throw new CodecException(CodecError.TrailingData, offset, "Trailing gzip data");
            } while (offset < source.Length);
        }
        VerifyContent(source, codec, output);
        return new(output.ToArray(), offset, members);
    }

    public static DecodedFrame Decode(Stream source, NbtCompression codec, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= DecodeLimits.Default; limits.Validate(); CheckCodec(codec);
        using var input = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = source.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, limits.MaxCompressedBytes - input.Length + 1)));
            if (read == 0) break;
            if (read > limits.MaxCompressedBytes - input.Length) throw Limit(input.Length);
            input.Write(buffer, 0, read);
        }
        return Decode(input.ToArray(), codec, limits);
    }

    public static byte[] Encode(byte[] decoded, NbtCompression codec, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        limits ??= DecodeLimits.Default; limits.Validate(); CheckCodec(codec);
        if (decoded.LongLength > limits.MaxDecodedBytes) throw Limit(decoded.Length);
        using var output = new LimitedOutput(limits.MaxCompressedBytes);
        if (codec == NbtCompression.Raw) output.Write(decoded);
        else if (decoded.Length == 0)
        {
            using DeflaterOutputStream emptyWriter = codec == NbtCompression.GZip
                ? new GZipOutputStream(output) { IsStreamOwner = false, ModifiedTime = DateTime.UnixEpoch }
                : new DeflaterOutputStream(output, new Deflater()) { IsStreamOwner = false };
            emptyWriter.Finish();
        }
        else
        {
            using Stream writer = codec == NbtCompression.GZip
                ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)
                : new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
            writer.Write(decoded);
        }
        return output.ToArray();
    }

    private static uint Inflate(byte[] source, ref int offset, MemoryStream output, DecodeLimits limits, Inflater inflater, byte[] buffer, Crc32 crc)
    {
        inflater.Reset(); crc.Reset();
        inflater.SetInput(source, offset, source.Length - offset);
        while (!inflater.IsFinished)
        {
            int read;
            try
            {
                read = inflater.Inflate(buffer, 0, (int)Math.Min(buffer.Length, limits.MaxDecodedBytes - output.Length + 1));
            }
            // 1.4.2 also throws IndexOutOfRange for malformed dynamic Huffman trees.
            catch (Exception error) when (error is SharpZipBaseException or IndexOutOfRangeException) { throw Corrupt(offset + inflater.TotalIn); }
            if (read > limits.MaxDecodedBytes - output.Length) throw Limit(offset + inflater.TotalIn);
            output.Write(buffer, 0, read); crc.Update(new ArraySegment<byte>(buffer, 0, read));
            if (read == 0 && !inflater.IsFinished) throw Corrupt(offset + inflater.TotalIn);
        }
        offset = source.Length - inflater.RemainingInput;
        return (uint)crc.Value;
    }

    private static void ReadGzipHeader(byte[] source, ref int offset)
    {
        var start = offset; Need(source, offset, 10);
        var flags = source[offset + 3];
        if (source[offset] != 0x1f || source[offset + 1] != 0x8b || source[offset + 2] != 8 || (flags & 0xe0) != 0)
            throw Corrupt(offset);
        offset += 10;
        if ((flags & 4) != 0)
        {
            Need(source, offset, 2); var length = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset)); offset += 2;
            Need(source, offset, length); offset += length;
        }
        foreach (var flag in new[] { 8, 16 })
            if ((flags & flag) != 0)
            {
                do { Need(source, offset, 1); } while (source[offset++] != 0);
            }
        if ((flags & 2) != 0)
        {
            Need(source, offset, 2); var crc = new Crc32(); crc.Update(new ArraySegment<byte>(source, start, offset - start));
            if ((ushort)crc.Value != BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset))) throw Corrupt(offset);
            offset += 2;
        }
    }

    private static void VerifyContent(byte[] source, NbtCompression codec, MemoryStream expected)
    {
        // SharpZipLib 1.4.2 permits distances before history; BCL rejects them.
        // Both readers are required: BCL alone cannot establish complete framing.
        using var input = new MemoryStream(source, writable: false);
        using Stream reader = codec == NbtCompression.GZip
            ? new GZipStream(input, CompressionMode.Decompress)
            : new ZLibStream(input, CompressionMode.Decompress);
        var buffer = new byte[8192]; var offset = 0;
        try
        {
            while (true)
            {
                var read = reader.Read(buffer);
                if (read == 0) break;
                if (read > expected.Length - offset || !buffer.AsSpan(0, read).SequenceEqual(expected.GetBuffer().AsSpan(offset, read))) throw Corrupt(input.Position);
                offset += read;
            }
            if (offset != expected.Length) throw Corrupt(input.Position);
        }
        catch (InvalidDataException) { throw Corrupt(input.Position); }
    }

    private static void Need(byte[] source, int offset, int count)
    {
        if (count > source.Length - offset) throw Corrupt(offset);
    }
    private static void CheckCodec(NbtCompression codec)
    {
        if (CompressionCapability.For((byte)codec).Read != SupportLevel.Supported)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unsupported compression codec");
    }
    private static CodecException Corrupt(long offset) => new(CodecError.CorruptCompression, offset, "Incomplete or corrupt compression frame");
    private static CodecException Limit(long offset) => new(CodecError.LimitExceeded, offset, "Compression resource limit exceeded");

    private sealed class LimitedOutput(long limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > limit - Length) throw Limit(Length);
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > limit - Length) throw Limit(Length);
            base.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            if (Length == limit) throw Limit(Length);
            base.WriteByte(value);
        }
    }
}
