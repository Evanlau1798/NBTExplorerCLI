using System.Text.Json;
using Next.Domain;

namespace Next.Formats;

public static class NativeJsonKeyEdits
{
    public static byte[] Convert(byte[] source, IEnumerable<(TypedPath Component, string ExpectedText, string Key, bool PercentArgument)> changes, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(changes);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (source.Length > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "JSON key source exceeds decode budget");
        var requested = changes.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(item => item.Component is null || item.ExpectedText is null || string.IsNullOrWhiteSpace(item.Key))) throw new ArgumentException("Invalid or oversized JSON key conversion set", nameof(changes));
        var snapshot = source.ToArray(); var spans = NativeJsonValueSpans.Read(snapshot, limits);
        using var document = BoundedJson.ReadDecoded(snapshot, limits);
        var edits = new List<(SnbtSpan Span, byte[] Bytes)>(); var seen = new HashSet<TypedPath>(); long length = snapshot.Length; long retained = 0;
        void Add(SnbtSpan span, byte[] bytes)
        {
            retained = checked(retained + bytes.Length); length = checked(length - span.LengthBytes + bytes.Length);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, span.StartByte, "JSON key conversion buffers exceed aggregate budget");
            edits.Add((span, bytes));
        }
        foreach (var change in requested)
        {
            if (!seen.Add(change.Component) || !spans.TryGetValue(change.Component, out var span)) throw new EditRejectedException(EditFailure.Precondition, "JSON component root is missing or duplicated");
            var value = Resolve(document.RootElement, change.Component);
            var key = NativeJsonEdits.ReplaceStrings("\"\""u8.ToArray(), [(TypedPath.Root, "", change.Key)], limits);
            if (value.ValueKind == JsonValueKind.String)
            {
                if (NativeJsonStrings.Read(value) != change.ExpectedText) throw new EditRejectedException(EditFailure.Precondition, "JSON literal differs from its source slot");
                byte[] with = change.PercentArgument ? ",\"with\":[\"%\"]"u8.ToArray() : [];
                Add(span, [.. "{\"translate\":"u8, .. key, .. with, (byte)'}']); continue;
            }
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetNativeProperty("text", out var text) || text.ValueKind != JsonValueKind.String || NativeJsonStrings.Read(text) != change.ExpectedText
                || new[] { "translate", "with", "fallback", "score", "selector", "keybind", "nbt" }.Any(name => value.TryGetNativeProperty(name, out _)))
                throw new EditRejectedException(EditFailure.Precondition, "Expected an unambiguous literal JSON component without active translation fields");
            var reader = new Utf8JsonReader(snapshot.AsSpan(span.StartByte, span.LengthBytes), new JsonReaderOptions { MaxDepth = limits.MaxDepth + 2 }); _ = reader.Read();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = NativeJsonStrings.ReadToken(ref reader); var nameStart = checked(span.StartByte + (int)reader.TokenStartIndex); var nameLength = reader.ValueSpan.Length + 2;
                _ = reader.Read();
                if (name == "text")
                {
                    Add(new(nameStart, nameLength), "\"translate\""u8.ToArray());
                    Add(new(checked(span.StartByte + (int)reader.TokenStartIndex), checked((int)(reader.BytesConsumed - reader.TokenStartIndex))), key);
                }
                reader.Skip();
            }
            if (change.PercentArgument) Add(new(span.EndByte - 1, 0), ",\"with\":[\"%\"]"u8.ToArray());
        }
        if (length > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "JSON key candidate exceeds output budget");
        var output = new byte[checked((int)length)]; var from = 0; var to = 0;
        foreach (var edit in edits.OrderBy(item => item.Span.StartByte))
        {
            if (edit.Span.StartByte < from) throw new EditRejectedException(EditFailure.Precondition, "JSON key conversion targets overlap");
            snapshot.AsSpan(from, edit.Span.StartByte - from).CopyTo(output.AsSpan(to)); to += edit.Span.StartByte - from;
            edit.Bytes.CopyTo(output.AsSpan(to)); to += edit.Bytes.Length; from = edit.Span.EndByte;
        }
        snapshot.AsSpan(from).CopyTo(output.AsSpan(to)); using var verified = BoundedJson.ReadDecoded(output, limits); return output;
    }
    private static JsonElement Resolve(JsonElement root, TypedPath path)
    {
        var value = root;
        foreach (var segment in path.Segments)
            if (segment.Kind == PathSegmentKind.Key && value.TryGetNativeProperty(segment.Key!, out var child)) value = child;
            else if (segment.Kind == PathSegmentKind.Index && value.ValueKind == JsonValueKind.Array && segment.Index!.Value < value.GetArrayLength()) value = value[segment.Index.Value];
            else throw new EditRejectedException(EditFailure.Precondition, "JSON component path differs from its source");
        return value;
    }
}
