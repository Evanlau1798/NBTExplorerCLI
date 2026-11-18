using System.Text.Json;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class NativeJsonValueSpans
{
    private sealed class Container(TypedPath path, bool array, int start)
    {
        internal readonly TypedPath Path = path;
        internal readonly bool Array = array;
        internal readonly int Start = start;
        internal int Index;
        internal string? Key;
    }
    public static Dictionary<TypedPath, SnbtSpan> Read(byte[] bytes, DecodeLimits? limits = null) => ReadCore(bytes, limits, false);
    internal static Dictionary<TypedPath, SnbtSpan> ReadStrings(byte[] bytes, DecodeLimits? limits) => ReadCore(bytes, limits, true);
    private static Dictionary<TypedPath, SnbtSpan> ReadCore(byte[] bytes, DecodeLimits? limits, bool stringsOnly)
    {
        limits ??= DecodeLimits.Default;
        using var validated = BoundedJson.ReadDecoded(bytes, limits);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions { MaxDepth = limits.MaxDepth + 2 });
        var result = new Dictionary<TypedPath, SnbtSpan>(); var stack = new Stack<Container>(); long retained = 0;
        void Add(TypedPath path, int start, int end)
        {
            retained = checked(retained + path.Segments.Length * 16L + 256);
            if (retained > DecodeLimits.Default.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, start, stringsOnly ? "JSON string locator retention budget exceeded" : "JSON value locator retention budget exceeded");
            result.Add(path, new(start, end - start));
        }
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName) { stack.Peek().Key = NativeJsonStrings.ReadToken(ref reader); continue; }
            var end = checked((int)reader.BytesConsumed + offset);
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                var container = stack.Pop(); if (!stringsOnly) Add(container.Path, container.Start, end); continue;
            }
            var path = TypedPath.Root;
            if (stack.TryPeek(out var parent)) path = new(parent.Path.Segments.Add(parent.Array ? PathSegment.ForIndex(parent.Index++) : PathSegment.ForKey(parent.Key!)));
            var start = checked((int)reader.TokenStartIndex + offset);
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) stack.Push(new(path, reader.TokenType == JsonTokenType.StartArray, start));
            else if (!stringsOnly || reader.TokenType == JsonTokenType.String) Add(path, start, end);
        }
        return result;
    }
}
