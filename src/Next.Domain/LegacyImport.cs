using System.Collections.Immutable;

namespace Next.Domain;

public enum LegacyInputProfile { CommandSession, TranslationSession, ReplyArray, Glossary, KeyMapping, AgentConfig, Questionnaire, NotesText, ScanCache, Unknown }
public enum LegacyImportDecision { Mapped, Conflict, RescanRequired, Unsupported, Rejected, Retained }
public sealed record LegacyRecord(int Index, string? Id, string? Original, string? Translation, string? Path, string? Type, string? Context, string? Dimension,
    Hash256? SourceHash, int LineCount, string? Problem = null);
public sealed record LegacyParsedInput(ImmutableArray<LegacyRecord> Records, string? Problem, string? WorldPath);
public sealed record LegacyImportItem(int Index, string? LegacyId, string? Original, string? Translation, string? LegacyPath, LegacyImportDecision Decision,
    string Reason, ImmutableArray<SourceAddress> Candidates, Hash256? LegacySourceHash, Hash256? CurrentSourceHash, Hash256? SlotId);
public sealed record LegacyImportBundle(Hash256 ImportId, LegacyInputProfile Profile, int ImporterVersion, Hash256 InputHash, string RawInputBase64,
    Guid WorldId, StableIdentity SnapshotId, string SourceLocale, string TargetLocale, bool RebindAcknowledged, ImmutableArray<LegacyImportItem> Items)
{
    public bool Complete => Items.All(item => item.Decision is LegacyImportDecision.Mapped or LegacyImportDecision.Retained);
}

public static class LegacyImportIdentity
{
    public static Hash256 Compute(LegacyInputProfile profile, Hash256 input, Guid world, StableIdentity snapshot, string sourceLocale, string targetLocale, bool acknowledged)
    {
        var fields = new[] { "legacy-import-v1", ((int)profile).ToString(System.Globalization.CultureInfo.InvariantCulture), input.Hex, world.ToString("D"), snapshot.Digest.Hex, sourceLocale, targetLocale, acknowledged ? "1" : "0" };
        return TextRevision.Compute(string.Concat(fields.Select(field => field.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + field)));
    }
}
