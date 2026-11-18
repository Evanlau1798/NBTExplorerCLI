using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

internal sealed class SnbtBudget(DecodeLimits limits)
{
    private long _nodes, _entries, _elements, _strings;
    public void Node(int depth, long offset)
    {
        if (depth > limits.MaxDepth || ++_nodes > limits.MaxNodes) Limit(offset);
    }
    public void Entry(long offset) { if (++_entries > limits.MaxContainerEntries) Limit(offset); }
    public void Element(long offset) { if (++_elements > limits.MaxCollectionElements) Limit(offset); }
    public void Text(string text, long offset)
    {
        try { _strings = checked(_strings + ModifiedUtf8.EncodedLength(text, limits.MaxMutf8StringBytes)); }
        catch (CodecException) { Limit(offset); }
        if (_strings > limits.MaxStringBytes) Limit(offset);
    }
    public static void Limit(long offset) => throw new SnbtException(SnbtError.LimitExceeded, offset, "SNBT resource budget exceeded");
}
