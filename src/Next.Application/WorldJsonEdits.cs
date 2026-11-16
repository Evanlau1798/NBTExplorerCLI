using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;

namespace Next.Application;

internal static class WorldJsonEdits
{
    internal static PreparedArtifactBytes Create(byte[] bytes, IReadOnlyList<PatchOperation> operations, DecodeLimits limits)
    {
        if (operations.Any(operation => operation is KeyConversionPatch)) return WorldJsonKeyEdits.Create(bytes, operations, limits);
        if (operations.Any(operation => operation is not LiteralPatch || operation.Targets.Any(address => address.JsonTarget != JsonTargetKind.Value))) throw new NotSupportedException("JSON preparation requires exact literal value slots");
        var literals = operations.Cast<LiteralPatch>().Select(operation => operation.Translation).ToArray();
        var changes = literals.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is null)
            .Select(item => (Path: item.Slot.Address.NodePath, Expected: item.Slot.SourceText, Replacement: item.Reply.Text)).ToList();
        var encoded = literals.Where(item => item.Slot.Address.Syntax is not null || item.Slot.Address.ComponentPath is not null).GroupBy(item => item.Slot.Address.NodePath).ToArray();
        var spans = encoded.Length == 0 ? null : NativeJsonStringSpans.Read(bytes, limits);
        long retainedCharacters = 0;
        foreach (var group in encoded)
        {
            if (!spans!.TryGetValue(group.Key, out var span)) throw new EditRejectedException(EditFailure.Precondition, "Encoded JSON target is not a unique string");
            var reader = new Utf8JsonReader(bytes.AsSpan(span.StartByte, span.LengthBytes)); _ = reader.Read(); var original = NativeJsonStrings.ReadToken(ref reader);
            string replacement;
            if (group.All(item => item.Slot.Address.Syntax is not null)) replacement = RewriteSnbt(original, group.ToArray(), limits);
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
        var changed = NativeJsonEdits.ReplaceStrings(bytes, translations.Select(item => (item.Slot.Address.ComponentPath!, item.Slot.SourceText, item.Reply.Text)), limits);
        return new UTF8Encoding(false, true).GetString(changed);
    }
    private static string RewriteSnbt(string original, IReadOnlyList<LiteralTranslation> translations, DecodeLimits limits)
    {
        var first = translations[0].Slot.Address.Syntax!;
        var grammar = first.GrammarProfile switch { "java-snbt-legacy" => SnbtProfile.JavaLegacy, "java-snbt-1.21.5" => SnbtProfile.Java1215, _ => throw new NotSupportedException("Unknown encoded SNBT grammar") };
        var parsed = Snbt.ParseDocument(new UTF8Encoding(false, true).GetBytes(original), grammar, limits);
        var changes = new List<(TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)>();
        long retainedCharacters = 0;
        foreach (var group in translations.GroupBy(item => item.Slot.Address.Syntax!.NodePath))
        {
            var syntax = group.First().Slot.Address.Syntax!;
            if (syntax.NodePath is null || syntax.PreimageHash != parsed.SourceHash.Hex) throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT source binding differs");
            var target = parsed.Resolve(syntax.NodePath);
            if (target.Span.StartByte != syntax.StartByte || target.Span.LengthBytes != syntax.LengthBytes || target.Value is not LogicalMinecraftLeaf { Value: NbtString text }) throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT target differs");
            string replacement;
            if (group.All(item => item.Slot.Address.ComponentPath is not null)) replacement = RewriteJson(text.Value, group.ToArray(), limits);
            else
            {
                var item = group.Single(); if (item.Slot.SourceText != text.Value) throw new EditRejectedException(EditFailure.Precondition, "SNBT literal preimage differs"); replacement = item.Reply.Text;
            }
            Retain(ref retainedCharacters, replacement.Length, limits);
            changes.Add((syntax.NodePath, target.Value, new LogicalMinecraftLeaf(new NbtString(replacement))));
        }
        return new UTF8Encoding(false, true).GetString(Snbt.ReplaceMany(parsed, changes));
    }
    private static void Retain(ref long characters, int next, DecodeLimits limits)
    {
        characters = checked(characters + next);
        // UTF-16 buffers use at most two bytes per retained code unit; one transform is separately codec-bounded.
        if (characters > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Encoded transformation strings exceed aggregate retention budget");
    }
}
