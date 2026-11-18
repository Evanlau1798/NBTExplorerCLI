using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class MinecraftNbtAdapter
{
    public static LogicalMinecraftDocument Decode(NbtDocument document, CapabilityProfile profile, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var mapping = Mapping(profile);
        _ = BinaryNbt.MeasureDocument(document, limits);
        return new(document.Name, DecodeValue(document.Root, mapping));
    }
    public static NbtDocument Encode(LogicalMinecraftDocument document, CapabilityProfile profile, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var mapping = Mapping(profile);
        var policy = limits ?? DecodeLimits.Default;
        policy.Validate();
        var root = new Encoder(mapping, policy).Value(document.Root, 0);
        var result = new NbtDocument(document.Name, root);
        _ = BinaryNbt.MeasureDocument(result, policy);
        return result;
    }
    private static LogicalListEncoding Mapping(CapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var mapping = profile.Lists;
        if (mapping.Encoding == LogicalListEncoding.Unknown
            || mapping.Encoding == LogicalListEncoding.CompoundWrapper && mapping.WrapperKey != "")
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unsupported logical NBT list mapping");
        return mapping.Encoding;
    }
    private static bool IsWrapper(NbtValue value) => value is NbtCompound { Entries.Length: 1 } compound && compound.Entries[0].Name == "";
    private static LogicalMinecraftValue DecodeValue(NbtValue value, LogicalListEncoding mapping) => value switch
    {
        NbtCompound compound => new LogicalMinecraftCompound(compound.Entries.Select(entry => new LogicalMinecraftEntry(entry.Name, DecodeValue(entry.Value, mapping)))),
        NbtList list => new LogicalMinecraftList(list.Items.Select(item => DecodeValue(
            mapping == LogicalListEncoding.CompoundWrapper && IsWrapper(item) ? ((NbtCompound)item).Entries[0].Value : item, mapping)),
            list.Items.IsEmpty ? list.ElementType : NbtType.End),
        _ => new LogicalMinecraftLeaf(value)
    };
    private sealed class Encoder(LogicalListEncoding mapping, DecodeLimits limits)
    {
        private long _nodes, _entries, _elements;
        public NbtValue Value(LogicalMinecraftValue value, int depth)
        {
            if (depth > limits.MaxDepth || ++_nodes > limits.MaxNodes) Limit();
            switch (value)
            {
                case LogicalMinecraftLeaf leaf: return leaf.Value;
                case LogicalMinecraftCompound compound:
                    _entries = checked(_entries + compound.Entries.Length);
                    if (_entries > limits.MaxContainerEntries) Limit();
                    return new NbtCompound(compound.Entries.Select(entry => new NbtEntry(entry.Name, Value(entry.Value, depth + 1))));
                case LogicalMinecraftList list:
                    _elements = checked(_elements + list.Items.Length);
                    if (_elements > limits.MaxCollectionElements || list.Items.Length > limits.MaxNodes - _nodes) Limit();
                    var items = list.Items.Select(item => Value(item, depth + 1)).ToArray();
                    if (items.Length == 0) return new NbtList(list.EmptyElementType, []);
                    var type = items[0].Type;
                    if (items.Any(item => item.Type != type))
                    {
                        if (mapping == LogicalListEncoding.Homogeneous)
                            throw new CodecException(CodecError.UnsupportedProfile, 0, "This profile requires homogeneous lists");
                        type = NbtType.Compound;
                    }
                    if (mapping == LogicalListEncoding.CompoundWrapper && type == NbtType.Compound)
                    {
                        // A legitimate singleton empty-key compound needs a second wrapper to survive one unwrap.
                        for (var index = 0; index < items.Length; index++)
                            if (items[index] is not NbtCompound || IsWrapper(items[index])) items[index] = new NbtCompound([new("", items[index])]);
                    }
                    return new NbtList(type, items);
                default: throw new ArgumentException("Unknown logical NBT value", nameof(value));
            }
        }
        private static void Limit() => throw new CodecException(CodecError.LimitExceeded, 0, "Logical NBT conversion exceeds resource limits");
    }
}
