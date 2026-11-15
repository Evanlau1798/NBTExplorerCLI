using System.Collections.Immutable;

namespace Next.Domain;

public abstract record LogicalMinecraftValue
{
    private protected LogicalMinecraftValue() { }
}

public sealed record LogicalMinecraftLeaf : LogicalMinecraftValue
{
    public NbtValue Value { get; }
    public LogicalMinecraftLeaf(NbtValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is NbtList or NbtCompound) throw new ArgumentException("Containers require recursive logical values", nameof(value));
        Value = value;
    }
}

public sealed record LogicalMinecraftList : LogicalMinecraftValue
{
    public ImmutableArray<LogicalMinecraftValue> Items { get; }
    public NbtType EmptyElementType { get; }
    public LogicalMinecraftList(IEnumerable<LogicalMinecraftValue> items, NbtType emptyElementType = NbtType.End)
    {
        Items = NbtSequence.Copy(items);
        if (Items.Any(item => item is null) || !Enum.IsDefined(emptyElementType)
            || Items.Length != 0 && emptyElementType != NbtType.End) throw new ArgumentException("Invalid logical list", nameof(items));
        EmptyElementType = emptyElementType;
    }
    public bool Equals(LogicalMinecraftList? other) => other is not null && EmptyElementType == other.EmptyElementType && Items.SequenceEqual(other.Items);
    public override int GetHashCode() => HashCode.Combine(EmptyElementType, NbtSequence.Hash(NbtType.List, Items));
}

public sealed record LogicalMinecraftEntry
{
    public string Name { get; }
    public LogicalMinecraftValue Value { get; }
    public LogicalMinecraftEntry(string name, LogicalMinecraftValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        (Name, Value) = (name, value);
    }
}

public sealed record LogicalMinecraftCompound : LogicalMinecraftValue
{
    public ImmutableArray<LogicalMinecraftEntry> Entries { get; }
    public LogicalMinecraftCompound(IEnumerable<LogicalMinecraftEntry> entries)
    {
        Entries = NbtSequence.Copy(entries);
        if (Entries.Any(entry => entry is null)) throw new ArgumentException("Null logical compound entry", nameof(entries));
    }
    public bool Equals(LogicalMinecraftCompound? other) => other is not null && Entries.SequenceEqual(other.Entries);
    public override int GetHashCode() => NbtSequence.Hash(NbtType.Compound, Entries);
}

public sealed record LogicalMinecraftDocument
{
    public string Name { get; }
    public LogicalMinecraftValue Root { get; }
    public LogicalMinecraftDocument(string name, LogicalMinecraftValue root)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(root);
        (Name, Root) = (name, root);
    }
}
