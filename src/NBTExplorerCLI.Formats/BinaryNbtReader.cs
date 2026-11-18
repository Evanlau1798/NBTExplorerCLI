using NBTExplorerCLI.Domain;
using System.Buffers.Binary;

namespace NBTExplorerCLI.Formats;

internal sealed class BinaryNbtReader(Stream input, DecodeLimits limits)
{
    private readonly CodecBudget _budget = new(limits);
    public NbtReadResult Read()
    {
        if (input.CanSeek && input.Length - input.Position > limits.MaxDecodedBytes)
            throw _budget.Error(CodecError.LimitExceeded, "Input exceeds decoded byte budget");
        var type = Tag(allowEnd: false);
        var name = String();
        var document = new NbtDocument(name, Payload(type, 0));
        if (input.ReadByte() >= 0)
        {
            _budget.CheckBytes(1);
            throw _budget.Error(CodecError.TrailingData, "Data follows the complete NBT root");
        }
        return new(document, _budget.Bytes, _budget.Metrics);
    }
    private void Ensure(long count)
    {
        _budget.CheckBytes(count);
        if (input.CanSeek && count > input.Length - input.Position) throw _budget.Error(CodecError.Truncated, "Declared payload exceeds remaining input");
    }
    private void Fill(Span<byte> buffer)
    {
        Ensure(buffer.Length);
        while (!buffer.IsEmpty)
        {
            var read = input.Read(buffer);
            if (read == 0) throw _budget.Error(CodecError.Truncated, "Unexpected end of NBT input");
            _budget.Consume(read);
            buffer = buffer[read..];
        }
    }
    private byte Byte() { Span<byte> bytes = stackalloc byte[1]; Fill(bytes); return bytes[0]; }
    private short Short() { Span<byte> bytes = stackalloc byte[2]; Fill(bytes); return BinaryPrimitives.ReadInt16BigEndian(bytes); }
    private int Int() { Span<byte> bytes = stackalloc byte[4]; Fill(bytes); return BinaryPrimitives.ReadInt32BigEndian(bytes); }
    private long Long() { Span<byte> bytes = stackalloc byte[8]; Fill(bytes); return BinaryPrimitives.ReadInt64BigEndian(bytes); }
    private NbtType Tag(bool allowEnd)
    {
        var offset = _budget.Bytes;
        var tag = (NbtType)Byte();
        if (!Enum.IsDefined(tag) || tag == NbtType.End && !allowEnd) throw _budget.Error(CodecError.InvalidTag, "Invalid NBT tag type", offset);
        return tag;
    }
    private string String()
    {
        var count = unchecked((ushort)Short());
        _budget.String(count);
        Ensure(count);
        var offset = _budget.Bytes;
        var bytes = new byte[count];
        Fill(bytes);
        return ModifiedUtf8.Decode(bytes, offset);
    }
    private NbtValue Payload(NbtType type, int depth)
    {
        _budget.Node(depth);
        return type switch
        {
            NbtType.Byte => new NbtByte(unchecked((sbyte)Byte())),
            NbtType.Short => new NbtShort(Short()),
            NbtType.Int => new NbtInt(Int()),
            NbtType.Long => new NbtLong(Long()),
            NbtType.Float => new NbtFloat(Int()),
            NbtType.Double => new NbtDouble(Long()),
            NbtType.String => new NbtString(String()),
            NbtType.ByteArray => ByteArray(),
            NbtType.IntArray => IntArray(),
            NbtType.LongArray => LongArray(),
            NbtType.List => List(depth),
            NbtType.Compound => Compound(depth),
            _ => throw _budget.Error(CodecError.InvalidTag, "End cannot be a payload")
        };
    }
    private int Count(int width)
    {
        var offset = _budget.Bytes;
        var count = Int();
        if (count < 0) throw _budget.Error(CodecError.InvalidLength, "Negative collection length", offset);
        _budget.Collection(count);
        Ensure(checked((long)count * width));
        return count;
    }
    private NbtByteArray ByteArray()
    {
        var values = new sbyte[Count(1)];
        for (var index = 0; index < values.Length; index++) values[index] = unchecked((sbyte)Byte());
        return new(values);
    }
    private NbtIntArray IntArray()
    {
        var values = new int[Count(4)];
        for (var index = 0; index < values.Length; index++) values[index] = Int();
        return new(values);
    }
    private NbtLongArray LongArray()
    {
        var values = new long[Count(8)];
        for (var index = 0; index < values.Length; index++) values[index] = Long();
        return new(values);
    }
    private NbtList List(int depth)
    {
        var offset = _budget.Bytes;
        var type = Tag(allowEnd: true);
        var count = Count(0);
        if (type == NbtType.End && count != 0) throw _budget.Error(CodecError.InvalidList, "Nonempty list cannot declare End elements", offset);
        _budget.CheckNodes(count);
        var width = type switch
        {
            NbtType.Byte or NbtType.Compound => 1,
            NbtType.Short or NbtType.String => 2,
            NbtType.Int or NbtType.Float or NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray => 4,
            NbtType.Long or NbtType.Double => 8,
            NbtType.List => 5,
            _ => 0
        };
        Ensure(checked((long)count * width));
        var values = new NbtValue[count];
        for (var index = 0; index < values.Length; index++) values[index] = Payload(type, depth + 1);
        return new(type, values);
    }
    private NbtCompound Compound(int depth)
    {
        var entries = new List<NbtEntry>();
        while (true)
        {
            var type = Tag(allowEnd: true);
            if (type == NbtType.End) return new(entries);
            _budget.Entry();
            var name = String();
            entries.Add(new(name, Payload(type, depth + 1)));
        }
    }
}
