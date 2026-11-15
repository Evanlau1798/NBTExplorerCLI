using Next.Domain;

namespace Next.Formats;

internal sealed class CodecBudget(DecodeLimits limits)
{
    public DecodeLimits Limits { get; } = limits;
    public long Bytes { get; private set; }
    private long _nodes, _entries, _elements, _strings;
    private int _depth;
    public CodecMetrics Metrics => new(Bytes, _nodes, _entries, _elements, _strings, _depth);
    public CodecException Error(CodecError code, string message, long? offset = null) => new(code, offset ?? Bytes, message);
    public void CheckBytes(long count)
    {
        if (count < 0 || count > Limits.MaxDecodedBytes - Bytes) throw Error(CodecError.LimitExceeded, "Decoded byte budget exceeded");
    }
    public void Consume(long count) { CheckBytes(count); Bytes += count; }
    public void Node(int depth)
    {
        if (depth > Limits.MaxDepth || _nodes >= Limits.MaxNodes) throw Error(CodecError.LimitExceeded, "Depth/node budget exceeded");
        _nodes++; _depth = Math.Max(_depth, depth);
    }
    public void CheckNodes(int count)
    {
        if (count > Limits.MaxNodes - _nodes) throw Error(CodecError.LimitExceeded, "Declared list exceeds node budget");
    }
    public void Entry()
    {
        if (_entries >= Limits.MaxContainerEntries) throw Error(CodecError.LimitExceeded, "Compound entry budget exceeded");
        _entries++;
    }
    public void Collection(int count)
    {
        if (count < 0) throw Error(CodecError.InvalidLength, "Negative collection length");
        if (count > Limits.MaxCollectionElements - _elements) throw Error(CodecError.LimitExceeded, "Collection element budget exceeded");
        _elements += count;
    }
    public void String(int count)
    {
        if (count > Limits.MaxMutf8StringBytes || count > Limits.MaxStringBytes - _strings)
            throw Error(CodecError.LimitExceeded, "String byte budget exceeded");
        _strings += count;
    }
}
