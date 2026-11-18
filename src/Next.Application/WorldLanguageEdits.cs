using System.Text.Json;
using Next.Domain;
using Next.Formats;

namespace Next.Application;

internal static class WorldLanguageEdits
{
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, byte[] bytes, IReadOnlyList<PatchOperation> operations, DecodeLimits limits)
    {
        if (operations.Any(operation => operation is not (LiteralPatch or KeyConversionPatch))) throw new NotSupportedException("Language output requires literal values or declared conversion dependencies");
        var literal = operations.OfType<LiteralPatch>().ToArray();
        var current = NativeJsonEdits.ReplaceStrings(bytes, literal.Select(operation => (operation.Translation.Slot.Address.NodePath, operation.Translation.Slot.SourceText, operation.Translation.Reply.Text)), limits);
        var dependencies = operations.OfType<KeyConversionPatch>().Select(operation => operation.Conversion.Dependency).ToArray();
        if (dependencies.Any(dependency => dependency.LanguageArtifact != artifact)) throw new EditRejectedException(EditFailure.Precondition, "Language output differs from its declared dependency");
        return AddValues(current, dependencies.Select(dependency => (dependency.Key, WorldKeyEdits.LanguageText(dependency.Text, limits))), limits);
    }
    internal static PreparedArtifactBytes AddValues(byte[] current, IEnumerable<(string Key, string Text)> values, DecodeLimits limits)
    {
        using var observation = WorkDiagnostics.Start("language.add");
        using var parsed = BoundedJson.ReadDecoded(current, limits);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new EditRejectedException(EditFailure.Precondition, "Language output must be a JSON object");
        var expected = parsed.RootElement.EnumerateObject().ToDictionary(NativeJsonStrings.PropertyName, item => NativeJsonStrings.Read(item.Value), StringComparer.Ordinal);
        var additions = new List<byte[]>(); long retained = current.Length; var hasProperties = expected.Count != 0;
        long comparisons = 0;
        foreach (var (name, text) in values)
        {
            comparisons++;
            observation?.SetTag("comparisons", comparisons);
            if (expected.TryGetValue(name, out var old))
            {
                if (old != text) throw new EditRejectedException(EditFailure.Precondition, "Language key already has different content");
                continue;
            }
            static byte[] Encode(string value, DecodeLimits policy) => NativeJsonEdits.ReplaceStrings("\"\""u8.ToArray(), [(TypedPath.Root, "", value)], policy);
            var key = Encode(name, limits); var value = Encode(text, limits);
            var length = checked((hasProperties ? 1 : 0) + key.Length + 1 + value.Length); retained = checked(retained + length);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Language additions exceed aggregate output budget");
            var addition = new byte[length]; var position = 0;
            if (hasProperties) addition[position++] = (byte)',';
            key.CopyTo(addition.AsSpan(position)); position += key.Length; addition[position++] = (byte)':'; value.CopyTo(addition.AsSpan(position));
            additions.Add(addition); expected.Add(name, text); hasProperties = true;
        }
        var output = current;
        if (additions.Count != 0)
        {
            var close = current.Length - 1; while (close >= 0 && current[close] is 32 or 9 or 13 or 10) close--;
            if (close < 0 || current[close] != (byte)'}') throw new EditRejectedException(EditFailure.Precondition, "Language object terminator is missing");
            output = new byte[checked((int)retained)]; current.AsSpan(0, close).CopyTo(output); var position = close;
            foreach (var addition in additions) { addition.CopyTo(output.AsSpan(position)); position += addition.Length; }
            current.AsSpan(close).CopyTo(output.AsSpan(position));
        }
        void Verify(Stream stream)
        {
            using var actual = BoundedJson.ReadDecoded(WorldArtifactEdits.Read(stream), limits);
            var values = actual.RootElement.EnumerateObject().ToDictionary(NativeJsonStrings.PropertyName, item => NativeJsonStrings.Read(item.Value), StringComparer.Ordinal);
            if (values.Count != expected.Count || expected.Any(item => !values.TryGetValue(item.Key, out var value) || value != item.Value)) throw new InvalidDataException("Language candidate differs from its declared values");
        }
        using (WorkDiagnostics.Start("language.projection", phase: "projection"))
            using (var stream = new MemoryStream(output, writable: false)) Verify(stream);
        observation?.SetTag("bytes", output.LongLength);
        return new(output, Verify);
    }
}
