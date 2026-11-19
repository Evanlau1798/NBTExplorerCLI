using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal static class WorldJsonEdits
{
    internal static PreparedArtifactBytes Create(byte[] bytes, IReadOnlyList<PatchOperation> operations, DecodeLimits limits, ArtifactAddress? artifact = null)
    {
        if (operations.Any(operation => operation is ReferencePatch))
        {
            if (artifact is null) throw new ArgumentException("References require an exact JSON artifact", nameof(artifact));
            operations = operations.Where(operation => operation is not ReferencePatch).Concat(WorldReferenceMembers.Literals(operations, artifact).Select((item, index) => new LiteralPatch("reference-" + index, item))).ToArray();
        }
        if (operations.Any(operation => operation is KeyConversionPatch)) return WorldJsonKeyEdits.Create(bytes, operations, limits);
        if (operations.Any(operation => operation is not LiteralPatch)) throw new NotSupportedException("JSON preparation requires exact literal value slots");
        return CreateLiterals(bytes, operations.Cast<LiteralPatch>().Select(operation => operation.Translation).ToArray(), limits);
    }
    internal static PreparedArtifactBytes CreateLiterals(byte[] bytes, IReadOnlyList<LiteralTranslation> literals, DecodeLimits limits)
    {
        if (literals.Any(item => item.Slot.Address.JsonTarget != JsonTargetKind.Value)) throw new NotSupportedException("JSON preparation requires literal value slots");
        var changes = LiteralChanges(bytes, literals.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is null),
            item => item.Slot.Address.NodePath, limits).ToList();
        var encoded = literals.Where(item => item.Slot.Address.Syntax is not null || item.Slot.Address.ComponentPath is not null).GroupBy(item => item.Slot.Address.NodePath).ToArray();
        var spans = encoded.Length == 0 ? null : NativeJsonStringSpans.Read(bytes, limits);
        long retainedCharacters = 0;
        foreach (var group in encoded)
        {
            if (!spans!.TryGetValue(group.Key, out var span)) throw new EditRejectedException(EditFailure.Precondition, "Encoded JSON target is not a unique string");
            var reader = new Utf8JsonReader(bytes.AsSpan(span.StartByte, span.LengthBytes)); _ = reader.Read(); var original = NativeJsonStrings.ReadToken(ref reader);
            string replacement;
            if (group.All(item => item.Slot.Address.Syntax is not null)) replacement = WorldKeyEdits.RewriteSnbt(original, [], limits, group.ToArray());
            else if (group.All(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is not null)) replacement = RewriteJson(original, group.ToArray(), limits);
            else throw new EditRejectedException(EditFailure.Precondition, "Encoded JSON layers conflict");
            Retain(ref retainedCharacters, replacement.Length, limits);
            changes.Add((group.Key, original, replacement));
        }
        var output = NativeJsonEdits.ReplaceStrings(bytes, changes, limits);
        return new(output, stream => { using var parsed = BoundedJson.ReadDecoded(WorldArtifactEdits.Read(stream), limits); });
    }
    internal static string RewriteJson(string original, IReadOnlyList<LiteralTranslation> translations, DecodeLimits limits)
    {
        if (translations.All(item => item.Slot.SourceText == item.Reply.Text)) return original;
        var bytes = NativeJsonStrings.EncodeJavaDocument(original, limits);
        var changed = NativeJsonEdits.ReplaceStrings(bytes, LiteralChanges(bytes, translations, item => item.Slot.Address.ComponentPath!, limits), limits);
        return new UTF8Encoding(false, true).GetString(changed);
    }
    internal static IEnumerable<(TypedPath Path, string Expected, string Replacement)> LiteralChanges(byte[] bytes,
        IEnumerable<LiteralTranslation> translations, Func<LiteralTranslation, TypedPath> locate, DecodeLimits limits)
    {
        Dictionary<TypedPath, SnbtSpan>? spans = null;
        foreach (var item in translations)
        {
            var path = locate(item);
            if (item.Slot.Role != ClickCommandText.Role) { yield return (path, item.Slot.SourceText, item.Reply.Text); continue; }
            spans ??= NativeJsonStringSpans.Read(bytes, limits);
            if (!spans.TryGetValue(path, out var span)) throw new EditRejectedException(EditFailure.Precondition, "JSON click command is not a string");
            var original = ReadString(bytes.AsSpan(span.StartByte, span.LengthBytes));
            yield return (path, original, ClickCommandText.Rewrite(original, item.Slot.SourceText, item.Reply.Text,
                path.Segments[^1].Key == "command", limits));
        }
    }
    internal static string ReadString(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new EditRejectedException(EditFailure.Precondition, "Expected a JSON string");
        return NativeJsonStrings.ReadToken(ref reader);
    }
    private static void Retain(ref long characters, int additional, DecodeLimits limits)
    {
        characters = checked(characters + additional);
        // UTF-16 buffers use at most two bytes per retained code unit; one transform is separately codec-bounded.
        if (characters > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Encoded transformation strings exceed aggregate retention budget");
    }
}
