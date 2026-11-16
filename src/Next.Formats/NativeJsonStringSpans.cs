using System.Text.Json;
using Next.Domain;

namespace Next.Formats;

public static class NativeJsonStringSpans
{
    private sealed class Container(TypedPath path, bool array)
    {
        internal readonly TypedPath Path = path;
        internal readonly bool Array = array;
        internal int Index;
        internal string? Key;
    }
    public static Dictionary<TypedPath, SnbtSpan> Read(byte[] bytes, DecodeLimits? limits = null)
    {
        limits ??= DecodeLimits.Default;
        using var validated = BoundedJson.ReadDecoded(bytes, limits);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions { MaxDepth = limits.MaxDepth + 2 });
        var result = new Dictionary<TypedPath, SnbtSpan>(); var stack = new Stack<Container>(); long retained = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName) { stack.Peek().Key = NativeJsonStrings.ReadToken(ref reader); continue; }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { stack.Pop(); continue; }
            var path = TypedPath.Root;
            if (stack.TryPeek(out var parent)) path = new(parent.Path.Segments.Add(parent.Array ? PathSegment.ForIndex(parent.Index++) : PathSegment.ForKey(parent.Key!)));
            if (reader.TokenType == JsonTokenType.String)
            {
                retained = checked(retained + path.Segments.Length * 16L + 256);
                if (retained > DecodeLimits.Default.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, reader.TokenStartIndex + offset, "JSON string locator retention budget exceeded");
                result.Add(path, new(checked((int)reader.TokenStartIndex + offset), checked((int)(reader.BytesConsumed - reader.TokenStartIndex))));
            }
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) stack.Push(new(path, reader.TokenType == JsonTokenType.StartArray));
        }
        return result;
    }
}
