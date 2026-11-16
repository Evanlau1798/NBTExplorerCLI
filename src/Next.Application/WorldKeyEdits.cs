using Next.Domain;
using Next.Formats;
using System.Text;

namespace Next.Application;

internal static class WorldKeyEdits
{
    internal static string RewriteJson(string source, IReadOnlyList<TranslationKeyConversion> conversions, DecodeLimits limits)
    {
        if (conversions.Any(conversion => conversion.Rule != KeyConversionRule.EncodedTextToTranslate
            || conversion.Address.ComponentPath is null || conversion.Expected is not NbtString expected || expected.Value != source))
            throw new EditRejectedException(EditFailure.Precondition, "Encoded component differs from its declared physical string");
        var output = NativeJsonKeyEdits.Convert(NativeJsonStrings.EncodeJavaDocument(source, limits), conversions.Select(conversion =>
            (conversion.Address.ComponentPath!, conversion.Slot.SourceText, conversion.Dependency.Key, conversion.Reply.Text.Contains('%'))), limits);
        return new UTF8Encoding(false, true).GetString(output);
    }
    internal static string RewriteSnbt(string source, IReadOnlyList<TranslationKeyConversion> conversions, DecodeLimits limits)
    {
        var grammar = conversions[0].Address.Syntax!.GrammarProfile switch
        {
            "java-snbt-legacy" => SnbtProfile.JavaLegacy,
            "java-snbt-1.21.5" => SnbtProfile.Java1215,
            _ => throw new NotSupportedException("Unknown encoded SNBT grammar")
        };
        var encoding = new UTF8Encoding(false, true); var parsed = Snbt.ParseDocument(encoding.GetBytes(source), grammar, limits);
        var changes = new List<(TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)>(); long retained = 0;
        foreach (var group in conversions.GroupBy(conversion => conversion.Address.Syntax!.NodePath))
        {
            var syntax = group.First().Address.Syntax!;
            var target = parsed.Resolve(syntax.NodePath!);
            if (syntax.PreimageHash != parsed.SourceHash.Hex || target.Span.StartByte != syntax.StartByte || target.Span.LengthBytes != syntax.LengthBytes
                || group.Any(conversion => conversion.Expected is not NbtString expected || expected.Value != source))
                throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT component binding differs");
            LogicalMinecraftValue replacement;
            if (group.All(conversion => conversion.Address.ComponentPath is not null) && target.Value is LogicalMinecraftLeaf { Value: NbtString text })
            {
                var normalized = group.Select(conversion => new TranslationKeyConversion(conversion.Address, text, conversion.Rule, conversion.Profile,
                    conversion.ProfileVersion, conversion.Slot, conversion.Reply, conversion.Dependency)).ToArray();
                var json = RewriteJson(text.Value, normalized, limits); retained = checked(retained + json.Length);
                replacement = new LogicalMinecraftLeaf(new NbtString(json));
            }
            else throw new NotSupportedException("SNBT key component encoding is not implemented");
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "SNBT key buffers exceed aggregate budget");
            changes.Add((syntax.NodePath!, target.Value, replacement));
        }
        return encoding.GetString(Snbt.ReplaceMany(parsed, changes));
    }
    internal static string LanguageText(string literal, DecodeLimits limits)
    {
        long characters = literal.Length;
        foreach (var unit in literal) if (unit == '%') characters += 3;
        if (characters > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Literal language pattern exceeds output budget");
        return literal.Replace("%", "%1$s", StringComparison.Ordinal);
    }
    internal static NbtValue Replacement(TranslationKeyConversion conversion)
    {
        var component = (NbtCompound)conversion.Replacement();
        if (!conversion.Reply.Text.Contains('%')) return component;
        // A literal percent argument avoids both language numeric normalization and component formatting.
        return new NbtCompound(component.Entries.Append(new("with", new NbtList(NbtType.String, [new NbtString("%")]))));
    }
}
