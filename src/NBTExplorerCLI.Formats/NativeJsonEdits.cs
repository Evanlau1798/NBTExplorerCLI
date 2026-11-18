using NBTExplorerCLI.Domain;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace NBTExplorerCLI.Formats;

public static class NativeJsonEdits
{
    public static byte[] ReplaceStrings(byte[] source, IEnumerable<(TypedPath Path, string Expected, string Replacement)> changes, DecodeLimits? limits = null)
    {
        using var observation = WorkDiagnostics.Start("json.rewrite");
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(changes);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (source.Length > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "JSON source exceeds decode budget");
        var requested = changes.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(change => change.Path is null || change.Expected is null || change.Replacement is null)) throw new ArgumentException("Invalid or oversized JSON replacement set", nameof(changes));
        var snapshot = source.ToArray(); var spans = NativeJsonStringSpans.Read(snapshot, limits);
        var seen = new HashSet<TypedPath>(); var edits = new List<(SnbtSpan Span, byte[] Bytes)>(); long length = snapshot.Length; long replacementBytes = 0;
        foreach (var change in requested)
        {
            if (!seen.Add(change.Path) || !spans.TryGetValue(change.Path, out var span)) throw new EditRejectedException(EditFailure.Precondition, "JSON string target is missing, duplicated or has another type");
            var reader = new Utf8JsonReader(snapshot.AsSpan(span.StartByte, span.LengthBytes)); _ = reader.Read();
            if (NativeJsonStrings.ReadToken(ref reader) != change.Expected) throw new EditRejectedException(EditFailure.Precondition, "JSON string differs from expected decoded value");
            if (change.Expected == change.Replacement) continue;
            var bytes = Encode(change.Replacement, limits.MaxDecodedBytes - replacementBytes); replacementBytes += bytes.Length;
            length = checked(length - span.LengthBytes + bytes.Length);
            edits.Add((span, bytes));
        }
        if (length > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "JSON candidate exceeds decode budget");
        observation?.SetTag("comparisons", (long)requested.Length);
        if (edits.Count == 0) { observation?.SetTag("bytes", snapshot.LongLength); return snapshot; }
        var output = new byte[checked((int)length)]; var from = 0; var to = 0;
        foreach (var edit in edits.OrderBy(edit => edit.Span.StartByte))
        {
            snapshot.AsSpan(from, edit.Span.StartByte - from).CopyTo(output.AsSpan(to)); to += edit.Span.StartByte - from;
            edit.Bytes.CopyTo(output.AsSpan(to)); to += edit.Bytes.Length; from = edit.Span.EndByte;
        }
        snapshot.AsSpan(from).CopyTo(output.AsSpan(to));
        using var verified = BoundedJson.ReadDecoded(output, limits);
        observation?.SetTag("bytes", output.LongLength);
        return output;
    }
    private static byte[] Encode(string text, long budget)
    {
        long bytes = 2;
        if (bytes > budget) throw new CodecException(CodecError.LimitExceeded, 0, "JSON replacements exceed decode budget");
        foreach (var unit in text)
        {
            bytes += char.IsSurrogate(unit) || unit < 32 ? 6 : unit is '"' or '\\' ? 2 : unit < 128 ? 1 : unit < 2048 ? 2 : 3;
            if (bytes > budget) throw new CodecException(CodecError.LimitExceeded, 0, "JSON replacements exceed decode budget");
        }
        var encoded = new StringBuilder(Math.Min(text.Length, 4096) + 2).Append('"');
        foreach (var unit in text)
            if (char.IsSurrogate(unit) || unit < 32) encoded.Append("\\u").Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
            else { if (unit is '"' or '\\') encoded.Append('\\'); encoded.Append(unit); }
        return Encoding.UTF8.GetBytes(encoded.Append('"').ToString());
    }
}
