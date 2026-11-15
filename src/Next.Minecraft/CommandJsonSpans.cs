using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

internal static class CommandJsonSpans
{
    private sealed class Container(TypedPath path, bool array) { internal TypedPath Path = path; internal bool Array = array; internal int Index; internal string? Key; }
    internal static Dictionary<TypedPath, SnbtSpan> Read(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 258 });
        var result = new Dictionary<TypedPath, SnbtSpan>(); var stack = new Stack<Container>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName) { stack.Peek().Key = NativeJsonStrings.ReadToken(ref reader); continue; }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { stack.Pop(); continue; }
            var path = TypedPath.Root;
            if (stack.TryPeek(out var parent)) path = parent.Array ? At(parent.Path, parent.Index++) : At(parent.Path, parent.Key!);
            result.Add(path, new(checked((int)reader.TokenStartIndex), checked((int)(reader.BytesConsumed - reader.TokenStartIndex))));
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) stack.Push(new(path, reader.TokenType == JsonTokenType.StartArray));
        }
        return result;
    }
}
