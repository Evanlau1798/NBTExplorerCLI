using System.Text;
using Next.Domain;
using Next.Formats;

namespace Next.Application;

internal static class WorldJsonKeyEdits
{
    internal static byte[] Convert(byte[] source, IReadOnlyList<TranslationKeyConversion> conversions, DecodeLimits limits)
    {
        var spans = NativeJsonValueSpans.Read(source, limits); var encoding = new UTF8Encoding(false, true);
        var direct = conversions.Where(conversion => conversion.Address.Syntax is null && conversion.Address.ComponentPath is null).ToArray();
        foreach (var conversion in direct)
        {
            var address = conversion.Address;
            if (conversion.Rule != KeyConversionRule.EncodedTextToTranslate || address.Syntax is not null || address.ComponentPath is not null
                || conversion.Expected is not NbtString expected || !spans.TryGetValue(address.NodePath, out var span)
                || encoding.GetString(source.AsSpan(span.StartByte, span.LengthBytes)) != expected.Value)
                throw new EditRejectedException(EditFailure.Precondition, "Native JSON component differs from its declared exact root");
        }
        var strings = new List<(TypedPath Path, string Expected, string Replacement)>(); long retained = 0;
        foreach (var group in conversions.Except(direct).GroupBy(conversion => conversion.Address.NodePath))
        {
            if (!spans.TryGetValue(group.Key, out var span)) throw new EditRejectedException(EditFailure.Precondition, "Encoded JSON source is missing");
            using var value = BoundedJson.ReadDecoded(source.AsSpan(span.StartByte, span.LengthBytes).ToArray(), limits);
            if (value.RootElement.ValueKind != System.Text.Json.JsonValueKind.String) throw new EditRejectedException(EditFailure.Precondition, "Encoded JSON source is not a string");
            var original = NativeJsonStrings.Read(value.RootElement);
            var replacement = group.All(conversion => conversion.Address.Syntax is not null) ? WorldKeyEdits.RewriteSnbt(original, group.ToArray(), limits)
                : group.All(conversion => conversion.Address.Syntax is null) ? WorldKeyEdits.RewriteJson(original, group.ToArray(), limits)
                : throw new EditRejectedException(EditFailure.Precondition, "Key conversion encoding layers conflict");
            retained = checked(retained + replacement.Length);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "JSON key buffers exceed aggregate budget");
            strings.Add((group.Key, original, replacement));
        }
        var changed = NativeJsonEdits.ReplaceStrings(source, strings, limits);
        return NativeJsonKeyEdits.Convert(changed, direct.Select(conversion =>
            (conversion.Address.NodePath, conversion.Slot.SourceText, conversion.Dependency.Key, conversion.Reply.Text.Contains('%'))), limits);
    }
    internal static PreparedArtifactBytes Create(byte[] source, IReadOnlyList<PatchOperation> operations, DecodeLimits limits)
    {
        if (operations.Any(operation => operation is not (LiteralPatch or KeyConversionPatch))) throw new NotSupportedException("JSON component preparation requires literal or key operations");
        var literal = operations.OfType<LiteralPatch>().Cast<PatchOperation>().ToArray();
        var current = WorldJsonEdits.Create(source, literal, limits).Bytes;
        var output = Convert(current, operations.OfType<KeyConversionPatch>().Select(operation => operation.Conversion).ToArray(), limits);
        return new(output, stream => { using var json = BoundedJson.ReadDecoded(WorldArtifactEdits.Read(stream), limits); });
    }
}
