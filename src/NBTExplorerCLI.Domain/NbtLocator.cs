namespace NBTExplorerCLI.Domain;

public enum ResolutionStatus { Found, Missing, Ambiguous, Invalid }
public sealed record NodeResolution(ResolutionStatus Status, NbtValue? Value, int SegmentOffset);

public static class NbtLocator
{
    public static NodeResolution Resolve(NbtValue root, TypedPath path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        var current = root;
        for (var offset = 0; offset < path.Segments.Length; offset++)
        {
            var segment = path.Segments[offset];
            if (segment.Kind == PathSegmentKind.Key)
            {
                if (current is not NbtCompound compound) return new(ResolutionStatus.Invalid, null, offset);
                NbtValue? found = null;
                foreach (var entry in compound.Entries)
                {
                    if (!string.Equals(entry.Name, segment.Key, StringComparison.Ordinal)) continue;
                    if (found is not null) return new(ResolutionStatus.Ambiguous, null, offset);
                    found = entry.Value;
                }
                if (found is null) return new(ResolutionStatus.Missing, null, offset);
                current = found;
            }
            else
            {
                var index = segment.Index!.Value;
                if (current is not (NbtList or NbtByteArray or NbtIntArray or NbtLongArray))
                    return new(ResolutionStatus.Invalid, null, offset);
                var found = current switch
                {
                    NbtList list when index < list.Items.Length => list.Items[index],
                    NbtByteArray array when index < array.Values.Length => new NbtByte(array.Values[index]),
                    NbtIntArray array when index < array.Values.Length => new NbtInt(array.Values[index]),
                    NbtLongArray array when index < array.Values.Length => new NbtLong(array.Values[index]),
                    _ => null
                };
                if (found is null) return new(ResolutionStatus.Missing, null, offset);
                current = found;
            }
        }
        return new(ResolutionStatus.Found, current, path.Segments.Length);
    }
}
