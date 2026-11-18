namespace NBTExplorerCLI.Domain;

public enum NbtType : byte
{
    End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
    ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12
}

public abstract record NbtValue
{
    public NbtType Type { get; }
    private protected NbtValue(NbtType type) => Type = type;
}

public sealed record NbtByte(sbyte Value) : NbtValue(NbtType.Byte);
public sealed record NbtShort(short Value) : NbtValue(NbtType.Short);
public sealed record NbtInt(int Value) : NbtValue(NbtType.Int);
public sealed record NbtLong(long Value) : NbtValue(NbtType.Long);
public sealed record NbtFloat(int RawBits) : NbtValue(NbtType.Float)
{
    public float Value => BitConverter.Int32BitsToSingle(RawBits);
    public static NbtFloat FromValue(float value) => new(BitConverter.SingleToInt32Bits(value));
}
public sealed record NbtDouble(long RawBits) : NbtValue(NbtType.Double)
{
    public double Value => BitConverter.Int64BitsToDouble(RawBits);
    public static NbtDouble FromValue(double value) => new(BitConverter.DoubleToInt64Bits(value));
}
public sealed record NbtString : NbtValue
{
    public string Value { get; }
    public NbtString(string value) : base(NbtType.String)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }
}

public sealed record NbtDocument
{
    public string Name { get; }
    public NbtValue Root { get; }
    public NbtDocument(string name, NbtValue root)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(root);
        (Name, Root) = (name, root);
    }
}
