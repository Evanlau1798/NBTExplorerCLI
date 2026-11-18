using System.Buffers;
using System.Buffers.Binary;

namespace NBTExplorerCLI.Domain;

internal sealed class IdentityWriter
{
    internal static ReadOnlySpan<byte> Header => "nbtcli.identity"u8;
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private readonly IdentityKind _kind;
    public IdentityWriter(IdentityKind kind)
    {
        _kind = kind;
        Raw(Header);
        Byte(1);
        Byte((byte)kind);
    }
    private Span<byte> Space(int count)
    {
        if (count < 0 || count > IdentityCanonical.MaximumBytes - _buffer.WrittenCount) throw new ArgumentException("Canonical identity exceeds 64 MiB");
        return _buffer.GetSpan(count)[..count];
    }
    public void Byte(byte value) { Space(1)[0] = value; _buffer.Advance(1); }
    public void Int(int value) { BinaryPrimitives.WriteInt32BigEndian(Space(4), value); _buffer.Advance(4); }
    public void Long(long value) { BinaryPrimitives.WriteInt64BigEndian(Space(8), value); _buffer.Advance(8); }
    private void Raw(ReadOnlySpan<byte> bytes) { bytes.CopyTo(Space(bytes.Length)); _buffer.Advance(bytes.Length); }
    public void Bytes(ReadOnlySpan<byte> bytes) { Int(bytes.Length); Raw(bytes); }
    public void Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Int(value.Length);
        var count = checked(value.Length * 2);
        var target = Space(count);
        for (var index = 0; index < value.Length; index++) BinaryPrimitives.WriteUInt16BigEndian(target.Slice(index * 2, 2), value[index]);
        _buffer.Advance(count);
    }
    public void Optional(string? value)
    {
        Byte(value is null ? (byte)0 : (byte)1);
        if (value is not null) Text(value);
    }
    public void Path(TypedPath? path)
    {
        Byte(path is null ? (byte)0 : (byte)1);
        if (path is null) return;
        Int(path.Segments.Length);
        foreach (var segment in path.Segments)
        {
            Byte((byte)segment.Kind);
            if (segment.Kind == PathSegmentKind.Key) Text(segment.Key!); else Int(segment.Index!.Value);
        }
    }
    public void Parent(StableIdentity identity, IdentityKind expected)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Kind != expected || !identity.IsValid) throw new ArgumentException("Invalid parent identity", nameof(identity));
        Bytes(identity.Canonical.AsSpan());
    }
    public void Set(IEnumerable<string> values)
    {
        var ordered = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Int(ordered.Length);
        foreach (var value in ordered) Text(value);
    }
    public StableIdentity Finish() => new(_kind, Hash256.Compute(_buffer.WrittenSpan), _buffer.WrittenSpan.ToArray());
}
