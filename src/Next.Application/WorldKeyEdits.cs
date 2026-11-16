using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Text;

namespace Next.Application;

internal static class WorldKeyEdits
{
    internal static string RewriteJson(string source, IReadOnlyList<TranslationKeyConversion> conversions, DecodeLimits limits, IReadOnlyList<LiteralTranslation>? translations = null)
    {
        translations ??= [];
        if (conversions.Count == 0) return WorldJsonEdits.RewriteJson(source, translations, limits);
        if (conversions.Any(conversion => conversion.Rule != KeyConversionRule.EncodedTextToTranslate
            || conversion.Address.ComponentPath is null || conversion.Expected is not NbtString expected || expected.Value != source))
            throw new EditRejectedException(EditFailure.Precondition, "Encoded component differs from its declared physical string");
        var output = NativeJsonKeyEdits.Convert(NativeJsonStrings.EncodeJavaDocument(source, limits), conversions.Select(conversion =>
            (conversion.Address.ComponentPath!, conversion.Slot.SourceText, conversion.Dependency.Key, conversion.Reply.Text.Contains('%'))), limits);
        var mixed = NativeJsonEdits.ReplaceStrings(output, translations.Select(item => (item.Slot.Address.ComponentPath!, item.Slot.SourceText, item.Reply.Text)), limits);
        return new UTF8Encoding(false, true).GetString(mixed);
    }
    internal static string RewriteSnbt(string source, IReadOnlyList<TranslationKeyConversion> conversions, DecodeLimits limits, IReadOnlyList<LiteralTranslation>? translations = null)
    {
        translations ??= [];
        if (conversions.Count == 0 && translations.Count == 0) return source;
        var first = conversions.Count == 0 ? translations[0].Slot.Address.Syntax! : conversions[0].Address.Syntax!;
        var grammar = first.GrammarProfile switch
        {
            "java-snbt-legacy" => SnbtProfile.JavaLegacy,
            "java-snbt-1.21.5" => SnbtProfile.Java1215,
            _ => throw new NotSupportedException("Unknown encoded SNBT grammar")
        };
        var encoding = new UTF8Encoding(false, true); var parsed = Snbt.ParseDocument(encoding.GetBytes(source), grammar, limits);
        var changes = new List<(TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)>(); long retained = 0;
        var literals = translations.ToLookup(item => item.Slot.Address.Syntax!.NodePath); var keyed = new HashSet<TypedPath>();
        (SnbtSpan Span, LogicalMinecraftValue Value) Resolve(SyntaxLocator syntax)
        {
            if (syntax.NodePath is null || syntax.PreimageHash != parsed.SourceHash.Hex || syntax.GrammarProfile != first.GrammarProfile || syntax.GrammarVersion != first.GrammarVersion)
                throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT source binding differs");
            var target = parsed.Resolve(syntax.NodePath);
            if (target.Span.StartByte != syntax.StartByte || target.Span.LengthBytes != syntax.LengthBytes) throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT target span differs");
            return (target.Span, target.Value);
        }
        void Retain(int units)
        {
            retained = checked(retained + units);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "SNBT transformation buffers exceed aggregate budget");
        }
        foreach (var group in conversions.GroupBy(conversion => conversion.Address.Syntax!.NodePath))
        {
            var syntax = group.First().Address.Syntax!;
            var target = Resolve(syntax); keyed.Add(syntax.NodePath!);
            if (group.Any(conversion => conversion.Expected is not NbtString expected || expected.Value != source))
                throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT component binding differs");
            var shared = literals[syntax.NodePath].ToArray(); foreach (var item in shared) _ = Resolve(item.Slot.Address.Syntax!);
            LogicalMinecraftValue replacement;
            if (group.All(conversion => conversion.Address.ComponentPath is not null) && target.Value is LogicalMinecraftLeaf { Value: NbtString text })
            {
                var normalized = group.Select(conversion => new TranslationKeyConversion(conversion.Address, text, conversion.Rule, conversion.Profile,
                    conversion.ProfileVersion, conversion.Slot, conversion.Reply, conversion.Dependency)).ToArray();
                if (shared.Any(item => item.Slot.Address.ComponentPath is null)) throw new EditRejectedException(EditFailure.Precondition, "Encoded SNBT component layers overlap");
                var json = RewriteJson(text.Value, normalized, limits, shared); Retain(json.Length);
                replacement = new LogicalMinecraftLeaf(new NbtString(json));
            }
            else if (group.Count() == 1 && group.First().Address.ComponentPath is null && shared.Length == 0)
            {
                var conversion = group.Single(); var profile = MinecraftProfiles.ForId(conversion.Profile) ?? throw new NotSupportedException("Unknown native SNBT component profile");
                if (profile.Features[ProfileFacet.TextSchema].Schema != "java-text-nbt") throw new NotSupportedException("SNBT component requires its native text profile");
                var document = MinecraftNbtAdapter.Encode(new("", target.Value), profile, limits);
                replacement = MinecraftNbtAdapter.Decode(new("", Component(document.Root, conversion)), profile, limits).Root;
                Retain(Snbt.Write(replacement, grammar, limits).Length);
            }
            else throw new EditRejectedException(EditFailure.Precondition, "SNBT key component encoding layers overlap");
            changes.Add((syntax.NodePath!, target.Value, replacement));
        }
        foreach (var group in literals.Where(group => group.Key is null || !keyed.Contains(group.Key)))
        {
            var syntax = group.First().Slot.Address.Syntax!; var target = Resolve(syntax);
            if (target.Value is not LogicalMinecraftLeaf { Value: NbtString text }) throw new EditRejectedException(EditFailure.Precondition, "SNBT literal target is not a string");
            string replacement;
            if (group.All(item => item.Slot.Address.ComponentPath is not null)) replacement = WorldJsonEdits.RewriteJson(text.Value, group.ToArray(), limits);
            else
            {
                if (group.Count() != 1 || group.First().Slot.SourceText != text.Value) throw new EditRejectedException(EditFailure.Precondition, "SNBT literal preimage or scope differs");
                replacement = group.First().Reply.Text;
            }
            Retain(replacement.Length); changes.Add((syntax.NodePath!, target.Value, new LogicalMinecraftLeaf(new NbtString(replacement))));
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
        => PercentArgument((NbtCompound)conversion.Replacement(), conversion.Reply.Text.Contains('%'));
    internal static NbtCompound Component(NbtValue source, TranslationKeyConversion conversion)
        => PercentArgument(TranslationKeyConversions.ToTranslate(source, conversion.Slot.SourceText, conversion.Dependency.Key), conversion.Reply.Text.Contains('%'));
    private static NbtCompound PercentArgument(NbtCompound component, bool percent)
    {
        if (!percent) return component;
        // A literal percent argument avoids both language numeric normalization and component formatting.
        return new NbtCompound(component.Entries.Append(new("with", new NbtList(NbtType.String, [new NbtString("%")]))));
    }
}
