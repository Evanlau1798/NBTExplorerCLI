namespace Next.Domain;

public enum KeyConversionRule { StructuredTextToTranslate, LiteralToTranslateComponent, EncodedTextToTranslate }

public sealed record LanguageDependency
{
    public ArtifactAddress LanguageArtifact { get; }
    public ArtifactAddress? PackArtifact { get; }
    public string Locale { get; }
    public string Key { get; }
    public string Text { get; }
    public LanguageDependency(ArtifactAddress languageArtifact, ArtifactAddress? packArtifact, string locale, string key, string text)
    {
        ArgumentNullException.ThrowIfNull(languageArtifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(text);
        if (languageArtifact.Kind is not (ArtifactKind.Json or ArtifactKind.Archive)
            || languageArtifact.Kind == ArtifactKind.Archive && (languageArtifact.ContainerEntry is null || packArtifact is null)
            || packArtifact is not null && (languageArtifact.Kind != ArtifactKind.Archive
                || packArtifact.WorldId != languageArtifact.WorldId || packArtifact.Kind != ArtifactKind.Archive
                || packArtifact.ContainerEntry is not null || packArtifact.RelativePath != languageArtifact.RelativePath))
            throw new ArgumentException("Expected an exact language output and its outer pack when archived");
        (LanguageArtifact, PackArtifact, Locale, Key, Text) = (languageArtifact, packArtifact, locale, key, text);
    }
}

public sealed record TranslationKeyConversion
{
    public SourceAddress Address { get; }
    public NbtValue Expected { get; }
    public KeyConversionRule Rule { get; }
    public string Profile { get; }
    public int ProfileVersion { get; }
    public TextSlot Slot { get; }
    public SlotReply Reply { get; }
    public LanguageDependency Dependency { get; }
    public TranslationKeyConversion(SourceAddress address, NbtValue expected, KeyConversionRule rule,
        string profile, int profileVersion, TextSlot slot, SlotReply reply, LanguageDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(profileVersion, 1);
        if (!Enum.IsDefined(rule)) throw new ArgumentOutOfRangeException(nameof(rule));
        (Address, Expected, Rule, Profile, ProfileVersion, Slot, Reply, Dependency)
            = (address, expected, rule, profile, profileVersion, slot, reply, dependency);
    }
    public NbtValue Replacement()
    {
        if (Rule == KeyConversionRule.EncodedTextToTranslate) throw new NotSupportedException("Encoded conversion requires its profile format adapter");
        NbtDocumentScope.RequireTarget(Address, Slot.Address);
        var path = Rule == KeyConversionRule.StructuredTextToTranslate
            ? new TypedPath(Address.NodePath.Segments.Add(PathSegment.ForKey("text"))) : Address.NodePath;
        if (!path.Equals(Slot.Address.NodePath) || Slot.Validate(Reply) != SlotValidation.Valid
            || Dependency.LanguageArtifact.WorldId != Address.Artifact.WorldId || Dependency.Locale != Slot.TargetLocale
            || Dependency.Text != Reply.Text)
            throw new ArgumentException("Conversion requires the exact slot, validated text and language dependency");
        if (Rule == KeyConversionRule.LiteralToTranslateComponent && Expected is not NbtString
            || Rule == KeyConversionRule.StructuredTextToTranslate && Expected is not NbtCompound) throw new ArgumentException("Component type differs from its conversion rule");
        return TranslationKeyConversions.ToTranslate(Expected, Slot.SourceText, Dependency.Key);
    }
    public void Validate()
    {
        if (Rule != KeyConversionRule.EncodedTextToTranslate) { _ = Replacement(); return; }
        if (Expected is not NbtString || Slot.Validate(Reply) != SlotValidation.Valid || Dependency.LanguageArtifact.WorldId != Address.Artifact.WorldId
            || Dependency.Locale != Slot.TargetLocale || Dependency.Text != Reply.Text || Address.Artifact != Slot.Address.Artifact
            || Address.Dimension != Slot.Address.Dimension || Address.Chunk != Slot.Address.Chunk || Address.SemanticRole != Slot.Role
            || Address.JsonTarget != JsonTargetKind.Value || Slot.Address.JsonTarget != JsonTargetKind.Value
            || Address.Artifact.Kind == ArtifactKind.Text || Address.Artifact.Kind == ArtifactKind.Archive && Address.Artifact.ContainerEntry is null
            || Address.Artifact.Kind is ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities && Address.ComponentPath is null && Address.Syntax is null
            || Address.Artifact.Kind == ArtifactKind.McFunction && Address.Syntax is null)
            throw new ArgumentException("Encoded conversion requires exact bound component, slot and language declarations");
        var syntax = Address.Syntax; var slotSyntax = Slot.Address.Syntax;
        if ((Address.ComponentPath is null) != (Slot.Address.ComponentPath is null)
            || (syntax is not null || Address.ComponentPath is not null) && !Address.NodePath.Equals(Slot.Address.NodePath)
            || Address.ComponentPath is not null && syntax is not null && (slotSyntax is null || syntax.NodePath is null || !syntax.NodePath.Equals(slotSyntax.NodePath)
                || syntax.StartByte != slotSyntax.StartByte || syntax.LengthBytes != slotSyntax.LengthBytes))
            throw new ArgumentException("Encoded conversion must retain its physical and decoded storage layers");
        if ((syntax is null) != (slotSyntax is null) || syntax is not null && (syntax.NodePath is null || slotSyntax!.NodePath is null
            || (syntax.GrammarProfile, syntax.GrammarVersion, syntax.PreimageHash, syntax.Encoding, syntax.Newline) != (slotSyntax.GrammarProfile, slotSyntax.GrammarVersion, slotSyntax.PreimageHash, slotSyntax.Encoding, slotSyntax.Newline)
            || syntax.StartByte > slotSyntax.StartByte || syntax.StartByte + syntax.LengthBytes < slotSyntax.StartByte + slotSyntax.LengthBytes))
            throw new ArgumentException("Encoded conversion syntax binding differs from its slot");
        static PathSegment[] LogicalPath(SourceAddress address) => [.. address.NodePath.Segments, .. address.Syntax?.NodePath?.Segments ?? [], .. address.ComponentPath?.Segments ?? []];
        var root = LogicalPath(Address); var leaf = LogicalPath(Slot.Address);
        if (!root.SequenceEqual(leaf.Take(root.Length)) || leaf.Length != root.Length && (leaf.Length != root.Length + 1 || leaf[^1] != PathSegment.ForKey("text")))
            throw new ArgumentException("Encoded conversion target must be the slot's literal component root");
    }
}

public static class TranslationKeyConversions
{
    public static NbtCompound ToTranslate(NbtValue original, string expectedText, string key)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(expectedText); ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (original is NbtString literal)
        {
            if (literal.Value != expectedText) throw new ArgumentException("Expected a matching literal string");
            return new([new("translate", new NbtString(key))]);
        }
        if (original is not NbtCompound component || component.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != component.Entries.Length
            || component.Entries.Any(entry => entry.Name is "translate" or "with" or "fallback"))
            throw new ArgumentException("Expected an unambiguous literal component without active translation fields");
        var entries = component.Entries; var index = -1;
        for (var position = 0; position < entries.Length; position++) if (entries[position].Name == "text") index = position;
        if (index < 0 || entries[index].Value is not NbtString text || text.Value != expectedText) throw new ArgumentException("Component literal differs from the declared slot");
        return new(entries.SetItem(index, new("translate", new NbtString(key))));
    }
    public static NbtDocument Apply(SourceAddress document, NbtDocument source, IEnumerable<TranslationKeyConversion> conversions)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        NbtDocumentScope.RequireDocument(document);
        return TypedTreeEdits.Apply(source, conversions.Select(conversion =>
        {
            ArgumentNullException.ThrowIfNull(conversion);
            if (conversion.Rule == KeyConversionRule.EncodedTextToTranslate) throw new NotSupportedException("Encoded conversion requires its profile format adapter");
            NbtDocumentScope.RequireTarget(document, conversion.Address);
            return TypedEdit.Replace(conversion.Address.NodePath, conversion.Expected, conversion.Replacement());
        }));
    }
    public static bool Verify(SourceAddress document, NbtDocument source, NbtDocument candidate, IEnumerable<TranslationKeyConversion> conversions)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Apply(document, source, conversions) == candidate;
    }
}
