namespace Next.Domain;

public enum KeyConversionRule { StructuredTextToTranslate, LiteralToTranslateComponent }

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
        NbtDocumentScope.RequireTarget(Address, Slot.Address);
        var path = Rule == KeyConversionRule.StructuredTextToTranslate
            ? new TypedPath(Address.NodePath.Segments.Add(PathSegment.ForKey("text"))) : Address.NodePath;
        if (!path.Equals(Slot.Address.NodePath) || Slot.Validate(Reply) != SlotValidation.Valid
            || Dependency.LanguageArtifact.WorldId != Address.Artifact.WorldId || Dependency.Locale != Slot.TargetLocale
            || Dependency.Text != Reply.Text)
            throw new ArgumentException("Conversion requires the exact slot, validated text and language dependency");
        if (Rule == KeyConversionRule.LiteralToTranslateComponent)
        {
            if (Expected is not NbtString literal || literal.Value != Slot.SourceText)
                throw new ArgumentException("Expected a matching literal string");
            return new NbtCompound([new("translate", new NbtString(Dependency.Key))]);
        }
        if (Expected is not NbtCompound component || component.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != component.Entries.Length
            || component.Entries.Any(entry => entry.Name is "translate" or "with" or "fallback"))
            throw new ArgumentException("Expected an unambiguous literal component without active translation fields");
        var entries = component.Entries;
        var index = -1;
        for (var position = 0; position < entries.Length; position++)
            if (entries[position].Name == "text") index = position;
        if (index < 0 || entries[index].Value is not NbtString text || text.Value != Slot.SourceText)
            throw new ArgumentException("Component literal differs from the declared slot");
        return new NbtCompound(entries.SetItem(index, new("translate", new NbtString(Dependency.Key))));
    }
}

public static class TranslationKeyConversions
{
    public static NbtDocument Apply(SourceAddress document, NbtDocument source, IEnumerable<TranslationKeyConversion> conversions)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        NbtDocumentScope.RequireDocument(document);
        return TypedTreeEdits.Apply(source, conversions.Select(conversion =>
        {
            ArgumentNullException.ThrowIfNull(conversion);
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
