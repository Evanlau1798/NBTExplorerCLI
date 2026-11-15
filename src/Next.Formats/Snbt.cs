using Next.Domain;
using System.Collections.Immutable;
using System.Text;

namespace Next.Formats;

public static class Snbt
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    public static SnbtDocument ParseDocument(byte[] source, SnbtProfile profile = SnbtProfile.JavaLegacy, DecodeLimits? limits = null)
        => Read(source, 0, true, profile, limits);
    public static SnbtDocument ParseFragment(byte[] source, int startByte, SnbtProfile profile = SnbtProfile.JavaLegacy, DecodeLimits? limits = null)
        => Read(source, startByte, false, profile, limits);
    private static SnbtDocument Read(byte[] source, int startByte, bool complete, SnbtProfile profile, DecodeLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        var policy = Checked(profile, limits);
        if (source.Length > policy.MaxDecodedBytes) SnbtBudget.Limit(0);
        return new SnbtReader(ImmutableArray.CreateRange(source), profile, policy).Read(startByte, complete);
    }
    public static byte[] Write(LogicalMinecraftValue value, SnbtProfile profile = SnbtProfile.JavaLegacy, DecodeLimits? limits = null)
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
        var parsed = document.IsComplete ? ParseDocument(output, document.Profile, document.Limits)
            : ParseFragment(output, document.ConsumedSpan.StartByte, document.Profile, document.Limits);
        if (parsed.Root.Value != changed || parsed.Root.Span.StartByte != document.Root.Span.StartByte
            || parsed.Root.Span.LengthBytes != document.Root.Span.LengthBytes - target.Span.LengthBytes + encoded.Length)
            throw new SnbtException(SnbtError.Precondition, target.Span.StartByte, "Replacement changes syntax outside its exact node");
        return output;
    }
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
