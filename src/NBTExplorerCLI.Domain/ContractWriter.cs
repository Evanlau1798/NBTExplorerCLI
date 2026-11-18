using System.Buffers;
using System.Buffers.Binary;

namespace NBTExplorerCLI.Domain;

internal sealed class ContractWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private int _nodes;
    public ContractWriter(string family)
    {
        Raw("nbtcli.contract"u8);
        Byte(1);
        Text(family);
    }
    private Span<byte> Space(int count)
    {
        if (count < 0 || count > 67_108_864 - _buffer.WrittenCount) throw new ArgumentException("Canonical contract exceeds 64 MiB");
        return _buffer.GetSpan(count)[..count];
    }
    private void Raw(ReadOnlySpan<byte> bytes) { bytes.CopyTo(Space(bytes.Length)); _buffer.Advance(bytes.Length); }
    public void Byte(byte value) { Space(1)[0] = value; _buffer.Advance(1); }
    public void Int(int value) { BinaryPrimitives.WriteInt32BigEndian(Space(4), value); _buffer.Advance(4); }
    public void Long(long value) { BinaryPrimitives.WriteInt64BigEndian(Space(8), value); _buffer.Advance(8); }
    private void Short(short value) { BinaryPrimitives.WriteInt16BigEndian(Space(2), value); _buffer.Advance(2); }
    public void Text(string text)
    {
        Int(text.Length);
        var bytes = Space(checked(text.Length * 2));
        for (var index = 0; index < text.Length; index++) BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(index * 2, 2), text[index]);
        _buffer.Advance(bytes.Length);
    }
    public void Optional(string? text) { Byte(text is null ? (byte)0 : (byte)1); if (text is not null) Text(text); }
    public void Hash(Hash256 hash) => Raw(Convert.FromHexString(hash.Hex));
    public void Identity(StableIdentity identity) { Int(identity.Canonical.Length); Raw(identity.Canonical.AsSpan()); }
    public void Artifact(ArtifactAddress artifact) => Identity(SourceIdentity.Artifact(artifact));
    public void Image(ArtifactImage image)
    {
        Byte(image.ExpectedAbsent ? (byte)0 : (byte)1);
        if (image.Hash is not null) Hash(image.Hash);
    }
    public void Address(SourceAddress address)
    {
        Identity(SourceIdentity.Occurrence(address));
        if (address.Syntax is not null)
        {
            Long(address.Syntax.StartByte);
            Long(address.Syntax.LengthBytes);
            Hash(new(address.Syntax.PreimageHash));
            Text(address.Syntax.Encoding);
            Text(address.Syntax.Newline);
        }
    }
    public void Path(TypedPath path)
    {
        Int(path.Segments.Length);
        foreach (var segment in path.Segments) Segment(segment);
    }
    public void Segment(PathSegment segment)
    {
        Byte((byte)segment.Kind);
        if (segment.Kind == PathSegmentKind.Key) Text(segment.Key!); else Int(segment.Index!.Value);
    }
    public void Value(NbtValue value, int depth = 0)
    {
        if (depth > 256 || ++_nodes > 100_000) throw new ArgumentException("Canonical NBT exceeds depth/node budget");
        Byte((byte)value.Type);
        switch (value)
        {
            case NbtByte number: Byte(unchecked((byte)number.Value)); break;
            case NbtShort number: Short(number.Value); break;
            case NbtInt number: Int(number.Value); break;
            case NbtLong number: Long(number.Value); break;
            case NbtFloat number: Int(number.RawBits); break;
            case NbtDouble number: Long(number.RawBits); break;
            case NbtString text: Text(text.Value); break;
            case NbtByteArray array:
                Int(array.Values.Length); foreach (var number in array.Values) Byte(unchecked((byte)number)); break;
            case NbtIntArray array:
                Int(array.Values.Length); foreach (var number in array.Values) Int(number); break;
            case NbtLongArray array:
                Int(array.Values.Length); foreach (var number in array.Values) Long(number); break;
            case NbtList list:
                Byte((byte)list.ElementType); Int(list.Items.Length); foreach (var item in list.Items) Value(item, depth + 1); break;
            case NbtCompound compound:
                Int(compound.Entries.Length); foreach (var entry in compound.Entries) { Text(entry.Name); Value(entry.Value, depth + 1); } break;
            default: throw new ArgumentException("Unknown NBT payload", nameof(value));
        }
    }
    public Hash256 Finish() => Hash256.Compute(_buffer.WrittenSpan);
}
