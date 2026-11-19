using NBTExplorerCLI.Domain;
using System.Collections.Immutable;

namespace NBTExplorerCLI.Formats;

public enum SnbtProfile { JavaThrough1214, Java1215, Java181, JavaMixedLists }
public enum SnbtError { UnexpectedEnd, InvalidToken, TrailingInput, InvalidEscape, InvalidNumber, MixedList, InvalidArrayElement, LimitExceeded, UnsupportedGrammar, UnsupportedValue, Missing, Ambiguous, Precondition }
public sealed class SnbtException(SnbtError code, long offset, string message) : Exception(message)
{
    public SnbtError Code { get; } = code;
    public long Offset { get; } = offset;
}
public readonly record struct SnbtSpan(int StartByte, int LengthBytes)
{
    public int EndByte => checked(StartByte + LengthBytes);
}
public sealed record SnbtChild(PathSegment Selector, SnbtSpan? KeySpan, SnbtNode Node);
public sealed class SnbtNode
{
    public LogicalMinecraftValue Value { get; }
    public SnbtSpan Span { get; }
    public ImmutableArray<SnbtChild> Children { get; }
    internal SnbtNode(LogicalMinecraftValue value, SnbtSpan span, IEnumerable<SnbtChild> children)
        => (Value, Span, Children) = (value, span, children.ToImmutableArray());
}
public sealed class SnbtDocument
{
    public ImmutableArray<byte> Source { get; }
    public Hash256 SourceHash { get; }
    public SnbtProfile Profile { get; }
    public SnbtNode Root { get; }
    public SnbtSpan ConsumedSpan { get; }
    public DecodeLimits Limits { get; }
    public bool IsComplete { get; }
    internal bool CommandTag { get; }
    internal SnbtDocument(ImmutableArray<byte> source, SnbtProfile profile, SnbtNode root, SnbtSpan consumed, DecodeLimits limits, bool complete, bool commandTag)
        => (Source, SourceHash, Profile, Root, ConsumedSpan, Limits, IsComplete, CommandTag) = (source, Hash256.Compute(source.AsSpan()), profile, root, consumed, limits, complete, commandTag);
    public SnbtNode Resolve(TypedPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var current = Root;
        foreach (var segment in path.Segments)
        {
            SnbtNode? found = null;
            foreach (var child in current.Children.Where(child => child.Selector == segment))
            {
                if (found is not null) throw new SnbtException(SnbtError.Ambiguous, current.Span.StartByte, "Duplicate syntax key");
                found = child.Node;
            }
            current = found ?? throw new SnbtException(SnbtError.Missing, current.Span.StartByte, "Syntax path not found");
        }
        return current;
    }
}
