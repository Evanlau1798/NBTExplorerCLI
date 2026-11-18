namespace NBTExplorerCLI.Domain;

public static class TypedTreeEdits
{
    public static NbtDocument Apply(NbtDocument source, IEnumerable<TypedEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edits);
        var ordered = edits.Take(100_001).ToArray();
        if (ordered.Length > 100_000 || ordered.Any(edit => edit is null)) throw new ArgumentException("Invalid edit collection", nameof(edits));
        Array.Sort(ordered, (left, right) => Compare(left.Path, right.Path));
        for (var index = 0; index < ordered.Length; index++)
        {
            var edit = ordered[index];
            if (index > 0 && IsPrefix(ordered[index - 1].Path, edit.Path))
                throw Reject(EditFailure.Overlap, "Overlapping edits require explicit resolution");
            var found = NbtLocator.Resolve(source.Root, edit.Path);
            if (found.Status != ResolutionStatus.Found) throw Reject(found.Status switch
            {
                ResolutionStatus.Missing => EditFailure.Missing,
                ResolutionStatus.Ambiguous => EditFailure.Ambiguous,
                _ => EditFailure.InvalidPath
            }, "Target is not a unique existing node");
            if (found.Value != edit.Expected) throw Reject(EditFailure.Precondition, "Expected typed value differs from preimage");
            if (edit.Action == TypedEditAction.Rename) ValidateRename(source.Root, edit);
        }
        var root = source.Root;
        // Snapshot indices stay valid when higher indices are removed first.
        for (var index = ordered.Length - 1; index >= 0; index--) root = ApplyAt(root, ordered[index], 0);
        return new(source.Name, root);
    }

    public static bool Verify(NbtDocument source, NbtDocument candidate, IEnumerable<TypedEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Apply(source, edits) == candidate;
    }

    private static NbtValue ApplyAt(NbtValue value, TypedEdit edit, int depth)
    {
        if (depth == edit.Path.Segments.Length) return edit.Action switch
        {
            TypedEditAction.Replace => edit.Replacement!,
            TypedEditAction.Add => Add(value, edit.Child!, edit.Replacement!),
            _ => throw Reject(EditFailure.InvalidPath, "Operation requires a parent")
        };
        var segment = edit.Path.Segments[depth];
        var terminal = depth + 1 == edit.Path.Segments.Length;
        if (segment.Kind == PathSegmentKind.Key && value is NbtCompound compound)
        {
            var index = -1;
            for (var position = 0; position < compound.Entries.Length; position++)
                if (compound.Entries[position].Name == segment.Key) index = position;
            if (index < 0) throw Reject(EditFailure.Missing, "Target disappeared");
            var entry = compound.Entries[index];
            if (terminal && edit.Action == TypedEditAction.Remove) return new NbtCompound(compound.Entries.RemoveAt(index));
            if (terminal && edit.Action == TypedEditAction.Rename)
            {
                if (compound.Entries.Where((_, position) => position != index).Any(other => other.Name == edit.NewName))
                    throw Reject(EditFailure.InvalidStructure, "Rename destination conflicts with another edit");
                return new NbtCompound(compound.Entries.SetItem(index, new(edit.NewName!, entry.Value)));
            }
            return new NbtCompound(compound.Entries.SetItem(index, new(entry.Name, ApplyAt(entry.Value, edit, depth + 1))));
        }
        if (segment.Kind != PathSegmentKind.Index) throw Reject(EditFailure.InvalidPath, "Invalid child selector");
        if (terminal && edit.Action == TypedEditAction.Rename) throw Reject(EditFailure.InvalidPath, "Only compound keys can be renamed");
        var itemIndex = segment.Index!.Value;
        if (terminal && edit.Action == TypedEditAction.Remove) return RemoveIndex(value, itemIndex);
        var child = value switch
        {
            NbtList list => list.Items[itemIndex],
            NbtByteArray array => new NbtByte(array.Values[itemIndex]),
            NbtIntArray array => new NbtInt(array.Values[itemIndex]),
            NbtLongArray array => new NbtLong(array.Values[itemIndex]),
            _ => throw Reject(EditFailure.InvalidPath, "Invalid indexed node")
        };
        var replacement = ApplyAt(child, edit, depth + 1);
        return value switch
        {
            NbtList list when replacement.Type == list.ElementType => new NbtList(list.ElementType, list.Items.SetItem(itemIndex, replacement)),
            NbtByteArray array when replacement is NbtByte number => new NbtByteArray(array.Values.SetItem(itemIndex, number.Value)),
            NbtIntArray array when replacement is NbtInt number => new NbtIntArray(array.Values.SetItem(itemIndex, number.Value)),
            NbtLongArray array when replacement is NbtLong number => new NbtLongArray(array.Values.SetItem(itemIndex, number.Value)),
            _ => throw Reject(EditFailure.InvalidStructure, "Element type must match physical list or array")
        };
    }

    private static NbtValue Add(NbtValue parent, PathSegment child, NbtValue value)
    {
        if (parent is NbtCompound compound && child.Kind == PathSegmentKind.Key)
        {
            if (compound.Entries.Any(entry => entry.Name == child.Key)) throw Reject(EditFailure.InvalidStructure, "Added key already exists");
            return new NbtCompound(compound.Entries.Add(new(child.Key!, value)));
        }
        if (child.Kind != PathSegmentKind.Index) throw Reject(EditFailure.InvalidPath, "Invalid added child selector");
        var index = child.Index!.Value;
        return parent switch
        {
            NbtList list when index <= list.Items.Length && value.Type == list.ElementType => new NbtList(list.ElementType, list.Items.Insert(index, value)),
            NbtByteArray array when index <= array.Values.Length && value is NbtByte number => new NbtByteArray(array.Values.Insert(index, number.Value)),
            NbtIntArray array when index <= array.Values.Length && value is NbtInt number => new NbtIntArray(array.Values.Insert(index, number.Value)),
            NbtLongArray array when index <= array.Values.Length && value is NbtLong number => new NbtLongArray(array.Values.Insert(index, number.Value)),
            _ => throw Reject(EditFailure.InvalidStructure, "Added element violates container bounds or type")
        };
    }

    private static NbtValue RemoveIndex(NbtValue parent, int index) => parent switch
    {
        NbtList list => new NbtList(list.ElementType, list.Items.RemoveAt(index)),
        NbtByteArray array => new NbtByteArray(array.Values.RemoveAt(index)),
        NbtIntArray array => new NbtIntArray(array.Values.RemoveAt(index)),
        NbtLongArray array => new NbtLongArray(array.Values.RemoveAt(index)),
        _ => throw Reject(EditFailure.InvalidPath, "Invalid indexed container")
    };

    private static void ValidateRename(NbtValue root, TypedEdit edit)
    {
        if (edit.Path.Segments[^1].Kind != PathSegmentKind.Key) throw Reject(EditFailure.InvalidPath, "Only compound keys can be renamed");
        var parentPath = new TypedPath(edit.Path.Segments.RemoveAt(edit.Path.Segments.Length - 1));
        var parent = (NbtCompound)NbtLocator.Resolve(root, parentPath).Value!;
        if (edit.NewName != edit.Path.Segments[^1].Key && parent.Entries.Any(entry => entry.Name == edit.NewName))
            throw Reject(EditFailure.InvalidStructure, "Rename destination already exists");
    }

    private static bool IsPrefix(TypedPath prefix, TypedPath path) => prefix.Segments.Length <= path.Segments.Length
        && prefix.Segments.SequenceEqual(path.Segments.Take(prefix.Segments.Length));
    private static int Compare(TypedPath left, TypedPath right)
    {
        for (var index = 0; index < Math.Min(left.Segments.Length, right.Segments.Length); index++)
        {
            var a = left.Segments[index];
            var b = right.Segments[index];
            var order = a.Kind.CompareTo(b.Kind);
            if (order == 0) order = a.Kind == PathSegmentKind.Key ? StringComparer.Ordinal.Compare(a.Key, b.Key) : a.Index!.Value.CompareTo(b.Index!.Value);
            if (order != 0) return order;
        }
        return left.Segments.Length.CompareTo(right.Segments.Length);
    }
    private static EditRejectedException Reject(EditFailure failure, string message) => new(failure, message);
}
