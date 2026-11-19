using NBTExplorerCLI.Domain;
using System.Collections.Immutable;
using System.Text;

namespace NBTExplorerCLI.Formats;

public static class Snbt
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    public static SnbtDocument ParseDocument(byte[] source, SnbtProfile profile = SnbtProfile.JavaThrough1214, DecodeLimits? limits = null)
        => Read(source, 0, true, profile, limits);
    public static SnbtDocument ParseFragment(byte[] source, int startByte, SnbtProfile profile = SnbtProfile.JavaThrough1214, DecodeLimits? limits = null)
        => Read(source, startByte, false, profile, limits);
    public static SnbtDocument ParseCommandTag(byte[] source, SnbtProfile profile, DecodeLimits limits)
        => Read(source, 0, true, profile, limits, commandTag: profile == SnbtProfile.JavaThrough1214);
    private static SnbtDocument Read(byte[] source, int startByte, bool complete, SnbtProfile profile, DecodeLimits? limits, bool commandTag = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var policy = Checked(profile, limits);
        if (source.Length > policy.MaxDecodedBytes) SnbtBudget.Limit(0);
        return new SnbtReader(ImmutableArray.CreateRange(source), profile, policy, commandTag).Read(startByte, complete);
    }
    public static byte[] Write(LogicalMinecraftValue value, SnbtProfile profile = SnbtProfile.JavaThrough1214, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SnbtWriter(Checked(profile, limits), profile).Write(value);
    }
    private static DecodeLimits Checked(SnbtProfile profile, DecodeLimits? limits)
    {
        if (!Enum.IsDefined(profile)) throw new SnbtException(SnbtError.UnsupportedGrammar, 0, "Unsupported SNBT grammar profile");
        var policy = limits ?? DecodeLimits.Default; policy.Validate(); return policy;
    }
    public static byte[] Replace(SnbtDocument document, TypedPath path, LogicalMinecraftValue expected, LogicalMinecraftValue replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        var target = document.Resolve(path);
        if (target.Value != expected) throw new SnbtException(SnbtError.Precondition, target.Span.StartByte, "Syntax value differs from expected preimage");
        LogicalMinecraftValue changed;
        try { changed = ReplaceValue(document.Root.Value, path, 0, expected, replacement); }
        catch (SnbtException error) when (error.Code == SnbtError.InvalidArrayElement)
        {
            throw new SnbtException(error.Code, target.Span.StartByte, error.Message);
        }
        var encoded = Write(replacement, document.Profile, document.Limits);
        var length = checked((long)document.Source.Length - target.Span.LengthBytes + encoded.Length);
        if (length > document.Limits.MaxDecodedBytes) SnbtBudget.Limit(target.Span.StartByte);
        var output = new byte[checked((int)length)];
        document.Source.AsSpan(0, target.Span.StartByte).CopyTo(output);
        encoded.CopyTo(output.AsSpan(target.Span.StartByte));
        document.Source.AsSpan()[target.Span.EndByte..].CopyTo(output.AsSpan(target.Span.StartByte + encoded.Length));
        var parsed = Reparse(document, output);
        if (parsed.Root.Value != changed || parsed.Root.Span.StartByte != document.Root.Span.StartByte
            || parsed.Root.Span.LengthBytes != document.Root.Span.LengthBytes - target.Span.LengthBytes + encoded.Length)
            throw new SnbtException(SnbtError.Precondition, target.Span.StartByte, "Replacement changes syntax outside its exact node");
        return output;
    }
    public static byte[] ReplaceMany(SnbtDocument document, IEnumerable<(TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)> changes)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(changes);
        var requested = changes.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(change => change.Path is null || change.Expected is null || change.Replacement is null)) throw new ArgumentException("Invalid or oversized SNBT replacement set", nameof(changes));
        var targets = requested.Select(change => (Change: change, Node: document.Resolve(change.Path))).OrderBy(item => item.Node.Span.StartByte).ToArray();
        var end = -1;
        foreach (var target in targets)
        {
            if (target.Node.Value != target.Change.Expected) throw new SnbtException(SnbtError.Precondition, target.Node.Span.StartByte, "SNBT node differs from expected value");
            if (target.Node.Span.StartByte < end) throw new SnbtException(SnbtError.Precondition, target.Node.Span.StartByte, "SNBT batch has overlapping targets");
            end = target.Node.Span.EndByte;
        }
        var changed = document.Root.Value; var edits = new List<(SnbtSpan Span, byte[] Bytes)>(); long length = document.Source.Length; long replacements = 0;
        foreach (var target in targets)
        {
            var change = target.Change; if (change.Expected == change.Replacement) continue;
            try { changed = ReplaceValue(changed, change.Path, 0, change.Expected, change.Replacement); }
            catch (SnbtException error) when (error.Code == SnbtError.InvalidArrayElement) { throw new SnbtException(error.Code, target.Node.Span.StartByte, error.Message); }
            var remaining = document.Limits.MaxDecodedBytes - replacements;
            if (remaining <= 0) SnbtBudget.Limit(target.Node.Span.StartByte);
            var bytes = Write(change.Replacement, document.Profile, document.Limits with { MaxDecodedBytes = remaining }); replacements += bytes.Length;
            length = checked(length - target.Node.Span.LengthBytes + bytes.Length); edits.Add((target.Node.Span, bytes));
        }
        if (length > document.Limits.MaxDecodedBytes || length > Array.MaxLength) SnbtBudget.Limit(document.Root.Span.StartByte);
        if (edits.Count == 0) return document.Source.ToArray();
        var output = new byte[checked((int)length)]; var from = 0; var to = 0;
        foreach (var edit in edits)
        {
            document.Source.AsSpan(from, edit.Span.StartByte - from).CopyTo(output.AsSpan(to)); to += edit.Span.StartByte - from;
            edit.Bytes.CopyTo(output.AsSpan(to)); to += edit.Bytes.Length; from = edit.Span.EndByte;
        }
        document.Source.AsSpan()[from..].CopyTo(output.AsSpan(to));
        var parsed = Reparse(document, output);
        if (parsed.Root.Value != changed || parsed.Root.Span.StartByte != document.Root.Span.StartByte
            || parsed.Root.Span.LengthBytes != document.Root.Span.LengthBytes + length - document.Source.Length)
            throw new SnbtException(SnbtError.Precondition, document.Root.Span.StartByte, "Batch changed syntax outside its authorized nodes");
        return output;
    }
    private static SnbtDocument Reparse(SnbtDocument document, byte[] output)
        => Read(output, document.IsComplete ? 0 : document.ConsumedSpan.StartByte, document.IsComplete, document.Profile, document.Limits, document.CommandTag);
    private static LogicalMinecraftValue ReplaceValue(LogicalMinecraftValue value, TypedPath path, int depth,
        LogicalMinecraftValue expected, LogicalMinecraftValue replacement)
    {
        if (depth == path.Segments.Length) return replacement;
        var segment = path.Segments[depth];
        return value switch
        {
            LogicalMinecraftCompound compound => new LogicalMinecraftCompound(compound.Entries.Select(entry => entry.Name == segment.Key
                ? new LogicalMinecraftEntry(entry.Name, ReplaceValue(entry.Value, path, depth + 1, expected, replacement)) : entry)),
            LogicalMinecraftList list => new LogicalMinecraftList(list.Items.SetItem(segment.Index!.Value,
                ReplaceValue(list.Items[segment.Index.Value], path, depth + 1, expected, replacement))),
            LogicalMinecraftLeaf leaf when expected is LogicalMinecraftLeaf oldLeaf && replacement is LogicalMinecraftLeaf newLeaf
                => ReplaceArray(leaf, new(path.Segments.Skip(depth)), oldLeaf.Value, newLeaf.Value),
            _ => throw new SnbtException(SnbtError.InvalidArrayElement, 0, "Array elements require matching scalar values")
        };
    }
    private static LogicalMinecraftValue ReplaceArray(LogicalMinecraftLeaf array, TypedPath path, NbtValue expected, NbtValue replacement)
    {
        try { return new LogicalMinecraftLeaf(TypedTreeEdits.Apply(new("", array.Value), [TypedEdit.Replace(path, expected, replacement)]).Root); }
        catch (EditRejectedException error) { throw new SnbtException(SnbtError.InvalidArrayElement, 0, error.Message); }
    }
}
