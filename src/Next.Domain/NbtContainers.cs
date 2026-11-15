using System.Collections.Immutable;

namespace Next.Domain;

public sealed record NbtByteArray : NbtValue
{
    public ImmutableArray<sbyte> Values { get; }
    public NbtByteArray(IEnumerable<sbyte> values) : base(NbtType.ByteArray) => Values = NbtSequence.Copy(values);
    public bool Equals(NbtByteArray? other) => other is not null && Values.SequenceEqual(other.Values);
    public override int GetHashCode() => NbtSequence.Hash(Type, Values);
}
public sealed record NbtIntArray : NbtValue
{
    public ImmutableArray<int> Values { get; }
    public NbtIntArray(IEnumerable<int> values) : base(NbtType.IntArray) => Values = NbtSequence.Copy(values);
    public bool Equals(NbtIntArray? other) => other is not null && Values.SequenceEqual(other.Values);
    public override int GetHashCode() => NbtSequence.Hash(Type, Values);
}
public sealed record NbtLongArray : NbtValue
{
    public ImmutableArray<long> Values { get; }
    public NbtLongArray(IEnumerable<long> values) : base(NbtType.LongArray) => Values = NbtSequence.Copy(values);
    public bool Equals(NbtLongArray? other) => other is not null && Values.SequenceEqual(other.Values);
    public override int GetHashCode() => NbtSequence.Hash(Type, Values);
}

public sealed record NbtList : NbtValue
{
    public NbtType ElementType { get; }
    public ImmutableArray<NbtValue> Items { get; }
    public NbtList(NbtType elementType, IEnumerable<NbtValue> items) : base(NbtType.List)
    {
        Items = NbtSequence.Copy(items);
        if (!Enum.IsDefined(elementType) || Items.Any(item => item is null || item.Type != elementType))
            throw new ArgumentException("Physical NBT list must have one declared payload type", nameof(items));
        ElementType = elementType;
    }
    public bool Equals(NbtList? other) => other is not null && ElementType == other.ElementType && Items.SequenceEqual(other.Items);
    public override int GetHashCode() => HashCode.Combine(ElementType, NbtSequence.Hash(Type, Items));
}

public sealed record NbtEntry
{
    public string Name { get; }
    public NbtValue Value { get; }
    public NbtEntry(string name, NbtValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        (Name, Value) = (name, value);
    }
}

public sealed record NbtCompound : NbtValue
{
    public ImmutableArray<NbtEntry> Entries { get; }
    public NbtCompound(IEnumerable<NbtEntry> entries) : base(NbtType.Compound)
    {
        Entries = NbtSequence.Copy(entries);
        if (Entries.Any(entry => entry is null)) throw new ArgumentException("Compound entry cannot be null", nameof(entries));
    }
    public bool Equals(NbtCompound? other) => other is not null && Entries.SequenceEqual(other.Entries);
    public override int GetHashCode() => NbtSequence.Hash(Type, Entries);
}

internal static class NbtSequence
{
    public static ImmutableArray<T> Copy<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = source.ToImmutableArray();
        if (values.IsDefault) throw new ArgumentException("Uninitialized immutable array", nameof(source));
        return values;
    }
    public static int Hash<T>(NbtType type, IEnumerable<T> values)
    {
        var hash = new HashCode();
        hash.Add(type);
        foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }
}
