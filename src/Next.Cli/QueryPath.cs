using System.Globalization;
using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;

namespace Next.Cli;

internal static class QueryPath
{
    private static readonly UTF8Encoding SegmentEncoding = new(false, true);
    internal static (QueryNode Node, TypedPath Path) Select(QueryNode root, CliArguments request, string? legacy)
    {
        if ((request.Flag("pointer") ? 1 : 0) + (request.Flag("segments") ? 1 : 0) + (legacy is null ? 0 : 1) > 1) throw new CliFailure("INVALID_ARGUMENTS", "Choose one node path format");
        var segments = new List<PathSegment>(); var node = root;
        void Step(PathSegment segment)
        {
            if (segments.Count >= 256) throw new CliFailure("RESOURCE_LIMIT", "Path depth exceeds 256");
            node = node.Child(segment); segments.Add(segment);
        }
        if (request.Get("segments") is { } encoded)
        {
            try
            {
                using var json = BoundedJson.ReadDecoded(SegmentEncoding.GetBytes(encoded), new(MaxDecodedBytes: 65_536));
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 256) throw new JsonException("Expected bounded typed segments");
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 || !item.TryGetProperty("kind", out var kind)) throw new JsonException("Invalid typed segment");
                    if (kind.GetString() == "key" && item.TryGetProperty("key", out var key)) Step(PathSegment.ForKey(NativeJsonStrings.Read(key)));
                    else if (kind.GetString() == "index" && item.TryGetProperty("index", out var index) && index.TryGetInt32(out var number) && number >= 0) Step(PathSegment.ForIndex(number));
                    else throw new JsonException("Invalid typed segment fields");
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { throw new CliFailure("INVALID_ARGUMENTS", error.Message); }
        }
        else if (request.Get("pointer") is { } pointer)
        {
            if (pointer != "" && !pointer.StartsWith('/')) throw new CliFailure("INVALID_ARGUMENTS", "JSON Pointer starts with /");
            foreach (var spelling in pointer == "" ? [] : pointer[1..].Split('/'))
            {
                var key = new StringBuilder();
                for (var i = 0; i < spelling.Length; i++)
                {
                    if (spelling[i] != '~') key.Append(spelling[i]);
                    else if (++i < spelling.Length && spelling[i] is '0' or '1') key.Append(spelling[i] == '0' ? '~' : '/');
                    else throw new CliFailure("INVALID_ARGUMENTS", "Invalid pointer escape");
                }
                var value = key.ToString();
                if (node.Array && value.Length > 1 && value[0] == '0') throw new CliFailure("INVALID_ARGUMENTS", "JSON Pointer array indices cannot have leading zeroes");
                Step(node.Array ? PathSegment.ForIndex(Index(value)) : PathSegment.ForKey(value));
            }
        }
        else if (legacy is { Length: > 0 })
        {
            for (var cursor = 0; cursor < legacy.Length;)
            {
                var remaining = legacy[cursor..];
                if ((remaining.Contains('.') || remaining.Contains('[')) && node.HasKey(remaining)) throw new CliFailure("AMBIGUOUS_PATH", "Use --pointer or --segments for punctuation in keys");
                if (legacy[cursor] == '[')
                {
                    var end = legacy.IndexOf(']', cursor + 1); if (end < 0) throw new CliFailure("INVALID_ARGUMENTS", "Unclosed array index");
                    Step(PathSegment.ForIndex(Index(legacy[(cursor + 1)..end]))); cursor = end + 1;
                }
                else
                {
                    var start = cursor; while (cursor < legacy.Length && legacy[cursor] is not ('.' or '[' or ']')) cursor++;
                    if (start == cursor) throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy key path"); Step(PathSegment.ForKey(legacy[start..cursor]));
                }
                if (cursor == legacy.Length) break;
                if (legacy[cursor] == '.') { cursor++; if (cursor == legacy.Length || legacy[cursor] is '.' or '[' or ']') throw new CliFailure("INVALID_ARGUMENTS", "Missing legacy key"); }
                else if (legacy[cursor] != '[') throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy path suffix");
            }
        }
        return (node, new(segments));
    }
    internal static TypedPath Append(TypedPath path, PathSegment child)
        => path.Segments.Length >= 256 ? throw new CliFailure("RESOURCE_LIMIT", "Query node addresses exceed depth 256") : new(path.Segments.Add(child));
    private static int Index(string value) => value.Length != 0 && value.All(char.IsAsciiDigit) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        ? number : throw new CliFailure("INVALID_ARGUMENTS", "Expected a nonnegative array index");
}
