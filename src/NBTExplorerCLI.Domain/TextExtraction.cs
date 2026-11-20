using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public sealed record TextOccurrence(SourceAddress Address, string Text, string Context, bool AllowedEmpty)
{
    public NbtType Type => NbtType.String;
    public TextSlot ToSlot(string sourceLocale, string targetLocale, PlaceholderPolicy? placeholders = null)
        => new(Address, Text, Context, sourceLocale, targetLocale, AllowedEmpty, placeholders ?? PlaceholderPolicy.None);
}
public sealed record TranslationKeyReference(SourceAddress Address, string Key);
public sealed record ExtractionDiagnostic(SourceAddress Address, string Code)
{
    public static bool IsCompatibilityWarning(string code) => code is "EmptyBiomeMusicSoundIdentifier" or "InvalidOptionalBiomeSoundIdentifier";
}
public sealed record CommandCandidate(SourceAddress Address, string Command);
public sealed record UnclassifiedTextCandidate(SourceAddress Address, string Text);
public static class TextExtractionBudget
{
    public static long RecordBytes(SourceAddress address, long codeUnits)
        => checked(codeUnits * 2 + (address.NodePath.Segments.Length + (address.ComponentPath?.Segments.Length ?? 0) + (address.Syntax?.NodePath?.Segments.Length ?? 0)) * 16L + 256);
    public static long ResultBytes(TextExtractionResult result, IEnumerable<NameReferenceCandidate>? references = null)
    {
        long bytes = 0;
        void Charge(SourceAddress address, string value) => bytes = checked(bytes + RecordBytes(address, value.Length));
        foreach (var text in result.Occurrences) Charge(text.Address, text.Text);
        foreach (var key in result.TranslationKeys) Charge(key.Address, key.Key);
        foreach (var diagnostic in result.Diagnostics) Charge(diagnostic.Address, diagnostic.Code);
        foreach (var command in result.PendingCommands) Charge(command.Address, command.Command);
        foreach (var unknown in result.Unclassified) Charge(unknown.Address, unknown.Text);
        foreach (var reference in references ?? []) Charge(reference.Address, reference.Name);
        return bytes;
    }
}
public enum StorageTextKind { Literal, TextComponent, InterpretedList }
public sealed record StorageTextRule(TypedPath Path, StorageTextKind Kind);
public sealed record TextExtractionResult(ImmutableArray<TextOccurrence> Occurrences,
    ImmutableArray<TranslationKeyReference> TranslationKeys, ImmutableArray<ExtractionDiagnostic> Diagnostics,
    ImmutableArray<CommandCandidate> PendingCommands, ImmutableArray<UnclassifiedTextCandidate> Unclassified)
{
    public bool Complete => PendingCommands.IsEmpty && Unclassified.IsEmpty
        && (Diagnostics.IsEmpty || Diagnostics.All(diagnostic => ExtractionDiagnostic.IsCompatibilityWarning(diagnostic.Code)));
}
