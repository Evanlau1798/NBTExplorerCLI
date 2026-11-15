using System.Collections.Immutable;

namespace Next.Domain;

public sealed record TextOccurrence(SourceAddress Address, string Text, string Context, bool AllowedEmpty)
{
    public NbtType Type => NbtType.String;
    public TextSlot ToSlot(string sourceLocale, string targetLocale)
        => new(Address, Text, Context, sourceLocale, targetLocale, AllowedEmpty, PlaceholderPolicy.None);
}
public sealed record TranslationKeyReference(SourceAddress Address, string Key);
public sealed record ExtractionDiagnostic(SourceAddress Address, string Code);
public sealed record CommandCandidate(SourceAddress Address, string Command);
public sealed record UnclassifiedTextCandidate(SourceAddress Address, string Text);
public static class TextExtractionBudget
{
    public static long RecordBytes(SourceAddress address, long codeUnits)
        => checked(codeUnits * 2 + (address.NodePath.Segments.Length + (address.ComponentPath?.Segments.Length ?? 0) + (address.Syntax?.NodePath?.Segments.Length ?? 0)) * 16L + 256);
}
public enum StorageTextKind { Literal, TextComponent }
public sealed record StorageTextRule(TypedPath Path, StorageTextKind Kind);
public sealed record TextExtractionResult(ImmutableArray<TextOccurrence> Occurrences,
    ImmutableArray<TranslationKeyReference> TranslationKeys, ImmutableArray<ExtractionDiagnostic> Diagnostics,
    ImmutableArray<CommandCandidate> PendingCommands, ImmutableArray<UnclassifiedTextCandidate> Unclassified)
{
    public bool Complete => Diagnostics.IsEmpty && PendingCommands.IsEmpty && Unclassified.IsEmpty;
}
