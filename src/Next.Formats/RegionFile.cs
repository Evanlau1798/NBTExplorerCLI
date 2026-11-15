using Next.Domain;
using System.Buffers.Binary;

namespace Next.Formats;

public sealed record RegionChunk(int Index, int SectorOffset, byte SectorCount, int PayloadLength, byte CodecId, bool External, uint Timestamp)
{
    public bool Present => SectorOffset != 0;
    public CompressionCapability Capability => CompressionCapability.For(CodecId, External);
}

public sealed class RegionFile
{
    public const int MaximumArtifactBytes = 67_108_864;
    private readonly byte[] snapshot;
    public IReadOnlyList<RegionChunk> Chunks { get; }
    private RegionFile(byte[] snapshot, RegionChunk[] chunks) => (this.snapshot, Chunks) = (snapshot, Array.AsReadOnly(chunks));

    public static RegionFile Read(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumArtifactBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Region artifact limit exceeded");
        if (source.Length < 8192 || source.Length % 4096 != 0) throw Invalid(0);
        var chunks = new RegionChunk[1024];
        for (var index = 0; index < chunks.Length; index++)
        {
            var location = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(index * 4));
            var sector = (int)(location >> 8); var count = (byte)(location & 255);
            var timestamp = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(4096 + index * 4));
            if ((sector == 0) != (count == 0)) throw Invalid(index * 4);
            if (sector == 0) { chunks[index] = new(index, 0, 0, 0, 0, false, timestamp); continue; }
            var start = checked((long)sector * 4096); var allocation = checked((long)count * 4096);
            if (sector < 2 || start > source.Length || allocation > source.Length - start) throw Invalid(index * 4);
            var length = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan((int)start));
            if (length < 1 || length > allocation - 4) throw Invalid(start);
            var flag = source[(int)start + 4]; var external = (flag & 128) != 0;
            if (external && length != 1) throw Invalid(start);
            chunks[index] = new(index, sector, count, (int)length - 1, (byte)(flag & 127), external, timestamp);
        }
        long end = 2;
        foreach (var chunk in chunks.Where(chunk => chunk.Present).OrderBy(chunk => chunk.SectorOffset))
        {
            if (chunk.SectorOffset < end) throw Invalid(chunk.Index * 4);
            end = checked((long)chunk.SectorOffset + chunk.SectorCount);
        }
        return new(source.ToArray(), chunks);
    }

    public byte[] CompressedPayload(int index)
    {
        var chunk = Existing(index);
        return snapshot.AsSpan(checked(chunk.SectorOffset * 4096 + 5), chunk.PayloadLength).ToArray();
    }

    public NbtReadResult ReadChunk(int index, DecodeLimits? limits = null)
    {
        var chunk = Existing(index); RequireSupported(chunk);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (chunk.PayloadLength > limits.MaxCompressedBytes)
            throw new CodecException(CodecError.LimitExceeded, (long)chunk.SectorOffset * 4096 + 5, "Chunk compressed payload limit exceeded");
        return CompressedNbt.ReadDocument(CompressedPayload(index), (NbtCompression)chunk.CodecId, limits);
    }

    public byte[] WithChunk(int index, NbtDocument document, NbtCompression codec, uint timestamp, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document); ValidateIndex(index);
        limits ??= DecodeLimits.Default; limits.Validate();
        var previous = Chunks[index];
        if (previous.Present)
        {
            RequireSupported(previous);
            var current = ReadChunk(index, limits).Document;
            if (previous.CodecId == (byte)codec && current == document) return snapshot.ToArray();
        }
        var payload = CompressedNbt.WriteDocument(document, codec, limits);
        var sectors = checked(((long)payload.Length + 5 + 4095) / 4096);
        var offset = (long)snapshot.Length / 4096;
        var length = checked(snapshot.Length + sectors * 4096);
        if (sectors > 255 || offset > 0xffffff || length > MaximumArtifactBytes)
            throw new CodecException(CodecError.LimitExceeded, snapshot.Length, "Region candidate exceeds sector or artifact capacity");
        var candidate = new byte[(int)length]; snapshot.CopyTo(candidate, 0);
        BinaryPrimitives.WriteUInt32BigEndian(candidate.AsSpan(index * 4), checked((uint)((offset << 8) | sectors)));
        BinaryPrimitives.WriteUInt32BigEndian(candidate.AsSpan(4096 + index * 4), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(candidate.AsSpan(snapshot.Length), checked((uint)payload.Length + 1));
        candidate[snapshot.Length + 4] = (byte)codec; payload.CopyTo(candidate, snapshot.Length + 5);
        return candidate;
    }

    private RegionChunk Existing(int index)
    {
        ValidateIndex(index); var chunk = Chunks[index];
        if (!chunk.Present) throw new KeyNotFoundException("Region chunk is absent");
        return chunk;
    }
    private static void ValidateIndex(int index)
    {
        if (index is < 0 or >= 1024) throw new ArgumentOutOfRangeException(nameof(index));
    }
    private static void RequireSupported(RegionChunk chunk)
    {
        if (chunk.Capability.Read != SupportLevel.Supported)
            throw new CodecException(CodecError.UnsupportedProfile, (long)chunk.SectorOffset * 4096 + 4, "Unsupported region compression or external chunk");
    }
    private static CodecException Invalid(long offset) => new(CodecError.InvalidRegion, offset, "Invalid region header, allocation or chunk descriptor");
}
