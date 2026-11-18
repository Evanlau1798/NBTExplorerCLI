using Next.Domain;
using System.Text;
using System.Text.Json;

namespace Next.Formats;

public static class BoundedJson
{
    public static JsonDocument ReadDecoded(byte[] source, DecodeLimits? limits = null)
    {
        using var observation = WorkDiagnostics.Start("json.parse");
        ArgumentNullException.ThrowIfNull(source);
        limits ??= DecodeLimits.Default; limits.Validate();
        void Check(long value, long maximum, long offset)
        {
            if (value > maximum) throw new CodecException(CodecError.LimitExceeded, offset, "JSON resource limit exceeded");
        }
        Check(source.LongLength, limits.MaxDecodedBytes, 0);
        try { _ = new UTF8Encoding(false, true).GetCharCount(source); }
        catch (DecoderFallbackException error) { throw new JsonException("Invalid JSON UTF8", error); }
        var start = source.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        var reader = new Utf8JsonReader(source.AsSpan(start), new JsonReaderOptions { MaxDepth = limits.MaxDepth + 2 });
        var containers = new Stack<HashSet<string>?>();
        long nodes = 0, entries = 0, elements = 0, strings = 0;
        while (reader.Read())
        {
            var type = reader.TokenType;
            var offset = reader.TokenStartIndex + start;
            if (type is JsonTokenType.EndObject or JsonTokenType.EndArray) { containers.Pop(); continue; }
            if (type is JsonTokenType.PropertyName or JsonTokenType.String)
            {
                strings = checked(strings + reader.ValueSpan.Length);
                Check(strings, limits.MaxStringBytes, offset);
                var value = NativeJsonStrings.ReadToken(ref reader);
                if (type == JsonTokenType.PropertyName)
                {
                    Check(++entries, limits.MaxContainerEntries, offset);
                    if (!containers.Peek()!.Add(value)) throw new JsonException("Duplicate JSON member");
                    continue;
                }
            }
            Check(++nodes, limits.MaxNodes, offset);
            Check(reader.CurrentDepth, limits.MaxDepth, offset);
            if (containers.Count != 0 && containers.Peek() is null) Check(++elements, limits.MaxCollectionElements, offset);
            if (type == JsonTokenType.StartObject) containers.Push(new(StringComparer.Ordinal));
            else if (type == JsonTokenType.StartArray) containers.Push(null);
        }
        var result = JsonDocument.Parse(source.AsMemory(start), new JsonDocumentOptions { MaxDepth = 257 });
        observation?.SetTag("bytes", source.LongLength);
        return result;
    }
}
