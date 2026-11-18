using NBTExplorerCLI.Domain;
using System.Buffers.Binary;

namespace NBTExplorerCLI.Formats;

internal sealed class BinaryNbtWriter(Stream? output, DecodeLimits limits)
{
    private readonly CodecBudget _budget = new(limits);
    public CodecMetrics Write(NbtDocument document)
    {
        Byte((byte)document.Root.Type);
        String(document.Name);
        Payload(document.Root, 0);
        return _budget.Metrics;
    }
    private void Raw(ReadOnlySpan<byte> bytes) { _budget.Consume(bytes.Length); output?.Write(bytes); }
    private void Byte(byte value) { Span<byte> bytes = stackalloc byte[1]; bytes[0] = value; Raw(bytes); }
    private void Short(short value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteInt16BigEndian(bytes, value); Raw(bytes); }
    private void Int(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); Raw(bytes); }
    private void Long(long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); Raw(bytes); }
    private void String(string text)
    {
        int count;
        try { count = ModifiedUtf8.EncodedLength(text, limits.MaxMutf8StringBytes); }
        catch (CodecException error) { throw new CodecException(error.Code, _budget.Bytes, error.Message); }
        _budget.String(count);
        Short(unchecked((short)count));
        if (output is null) _budget.Consume(count);
        else Raw(ModifiedUtf8.Encode(text, limits.MaxMutf8StringBytes));
    }
    private void Collection(int count, int width)
    {
        _budget.Collection(count);
        Int(count);
        _budget.CheckBytes(checked((long)count * width));
    }
    private void Payload(NbtValue value, int depth)
    {
        _budget.Node(depth);
        switch (value)
        {
            case NbtByte number: Byte(unchecked((byte)number.Value)); break;
            case NbtShort number: Short(number.Value); break;
            case NbtInt number: Int(number.Value); break;
            case NbtLong number: Long(number.Value); break;
            case NbtFloat number: Int(number.RawBits); break;
            case NbtDouble number: Long(number.RawBits); break;
            case NbtString text: String(text.Value); break;
            case NbtByteArray array:
                Collection(array.Values.Length, 1); foreach (var number in array.Values) Byte(unchecked((byte)number)); break;
            case NbtIntArray array:
                Collection(array.Values.Length, 4); foreach (var number in array.Values) Int(number); break;
            case NbtLongArray array:
                Collection(array.Values.Length, 8); foreach (var number in array.Values) Long(number); break;
            case NbtList list:
                Byte((byte)list.ElementType);
                Collection(list.Items.Length, 0);
                _budget.CheckNodes(list.Items.Length);
                foreach (var item in list.Items) Payload(item, depth + 1);
                break;
            case NbtCompound compound:
                foreach (var entry in compound.Entries)
                {
                    _budget.Entry();
                    Byte((byte)entry.Value.Type);
                    String(entry.Name);
                    Payload(entry.Value, depth + 1);
                }
                Byte(0);
                break;
            default: throw _budget.Error(CodecError.InvalidTag, "Unknown or invalid NBT payload");
        }
    }
}
