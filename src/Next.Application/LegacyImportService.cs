using Next.Domain;
using Next.Formats;
using System.Collections.Immutable;
using System.Text;

namespace Next.Application;

public sealed class LegacyImportService(ILegacyImportStore store)
{
    private readonly ILegacyImportStore store = store ?? throw new ArgumentNullException(nameof(store));
    public LegacyImportBundle Import(LegacyInputProfile profile, ReadOnlyMemory<byte> input, WorldQuerySnapshot snapshot, string sourceLocale, string targetLocale, bool acknowledgeWorldRebind)
    {
        var bundle = Evaluate(profile, input, snapshot, sourceLocale, targetLocale, acknowledgeWorldRebind);
        var bytes = Encoding.UTF8.GetBytes(LegacyImportJson.Write(bundle)); var prior = store.Read(bundle.ImportId);
        if (prior is not null)
        {
            if (!prior.AsSpan().SequenceEqual(bytes)) throw new ArtifactConflictException("Legacy report differs from current reconstruction");
            return LegacyImportJson.Read(new UTF8Encoding(false, true).GetString(prior));
        }
        store.Save(bundle.ImportId, bytes); return bundle;
    }
    public Guid CreateSession(LegacyImportBundle bundle, WorldQuerySnapshot snapshot, TranslationSessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(bundle); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(sessions);
        var stored = store.Read(bundle.ImportId) ?? throw new IOException("Missing durable legacy report");
        if (!stored.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(LegacyImportJson.Write(bundle)))) throw new IOException("Legacy report was changed");
        var reconstructed = Evaluate(bundle.Profile, Convert.FromBase64String(bundle.RawInputBase64), snapshot, bundle.SourceLocale, bundle.TargetLocale, bundle.RebindAcknowledged);
        if (LegacyImportJson.Write(reconstructed) != LegacyImportJson.Write(bundle)) throw new IOException("Legacy target snapshot or reconstruction changed");
        if (!bundle.Complete || bundle.Items.Any(item => item.Decision != LegacyImportDecision.Mapped)) throw new IOException("Unresolved legacy records cannot create a session");
        var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Select(item => item.ToSlot(bundle.SourceLocale, bundle.TargetLocale)).ToDictionary(slot => slot.Id.Digest);
        var selected = bundle.Items.Select(item => slots[item.SlotId!]).ToImmutableArray();
        var translations = bundle.Items.ToDictionary(item => item.SlotId!, item => item.Translation);
        var id = Operation(bundle.ImportId, "session"); var manifest = snapshot.Manifest;
        var source = new SessionSourceSnapshot(manifest.WorldId, manifest.SnapshotId, manifest.InputSources, manifest.Scope.Dimensions, manifest.Scope.Regions, manifest.Scope.Types, manifest.Scope.ArtifactPaths, manifest.Versions, manifest.CoverageComplete);
        var request = new SessionStartRequest(id, source, bundle.SourceLocale, bundle.TargetLocale, new(MaximumPageSlots: 1), selected);
        sessions.Start(request, Operation(bundle.ImportId, "start"));
        var allowed = Enumerable.Range(0, selected.Length).Select(page => Operation(bundle.ImportId, "page:" + page.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(Operation(bundle.ImportId, "start")).ToHashSet();
        for (var pageNumber = 0; pageNumber < selected.Length; pageNumber++)
        {
            var operation = Operation(bundle.ImportId, "page:" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var state = sessions.Read(id);
            if (state.Operations.Any(item => !allowed.Contains(item.OperationId))) throw new IOException("Imported session has subsequent user operations");
            if (state.Operations.Any(item => item.OperationId == operation)) continue;
            var page = SessionPageProjection.Create(state, pageNumber); var text = translations[page.Slots.Single().SlotId]; if (text is null) continue;
            var reply = new SessionReply(operation, id, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale,
                [new(page.Slots[0].SlotId, page.Slots[0].SourceTextHash, SessionReplyState.Translated, text)]);
            sessions.Submit(id, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
        }
        return id;
    }
    private static Guid Operation(Hash256 id, string purpose) => new(Convert.FromHexString(TextRevision.Compute(id.Hex + ":" + purpose).Hex).AsSpan(0, 16));
    private static LegacyImportBundle Evaluate(LegacyInputProfile profile, ReadOnlyMemory<byte> input, WorldQuerySnapshot snapshot, string sourceLocale, string targetLocale, bool acknowledged)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocale); ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);
        if (input.Length > LegacyInputReader.MaximumInputBytes) throw new ArgumentException("Legacy input exceeds byte budget", nameof(input));
        if (sourceLocale.Length > 128 || targetLocale.Length > 128) throw new ArgumentException("Legacy locale exceeds budget");
        var bytes = input.ToArray(); var parsed = LegacyInputReader.Read(profile, bytes); var manifest = snapshot.Manifest;
        var inputHash = Hash256.Compute(bytes); var id = LegacyImportIdentity.Compute(profile, inputHash, manifest.WorldId, manifest.SnapshotId, sourceLocale, targetLocale, acknowledged);
        var duplicates = parsed.Records.Where(record => record.Id is not null).GroupBy(record => record.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key!).ToHashSet(StringComparer.Ordinal);
        var available = snapshot.ReadSources().SelectMany(row => (row.Text?.Occurrences ?? []).Select(text => (Text: text, Hash: row.Source.ContentHash))).Take(100_001).ToArray();
        if (available.Length > 100_000) throw new IOException("Legacy mapping candidate budget exceeded");
        var locations = available.Where(row => row.Text.Address.Artifact.RelativePath == "level.dat" && row.Text.Address.Artifact.ContainerEntry is null
            && row.Text.Address.Syntax is null && row.Text.Address.Dimension is null && row.Text.Address.Chunk is null).ToLookup(row => row.Text.Address.NodePath);
        var retainedCandidates = 0;
        var items = parsed.Records.Select(record => Decide(record)).ToImmutableArray();
        var collided = items.Where(item => item.Decision == LegacyImportDecision.Mapped).GroupBy(item => item.SlotId!).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        items = items.Select(item => item.SlotId is not null && collided.Contains(item.SlotId) ? item with { Decision = LegacyImportDecision.Conflict, Reason = "DuplicateTargetSlot" } : item).ToImmutableArray();
        return new(id, profile, 1, inputHash, Convert.ToBase64String(bytes), manifest.WorldId, manifest.SnapshotId, sourceLocale, targetLocale, acknowledged, items);

        LegacyImportItem Decide(LegacyRecord record)
        {
            LegacyImportItem Result(LegacyImportDecision decision, string reason, ImmutableArray<SourceAddress> candidates = default, Hash256? current = null, Hash256? slot = null)
                => new(record.Index, record.Id, record.Original, record.Translation, record.Path, decision, reason, candidates.IsDefault ? [] : candidates, record.SourceHash, current, slot);
            if (record.Id is not null && duplicates.Contains(record.Id)) return Result(LegacyImportDecision.Conflict, "DuplicateLegacyId");
            if (profile is LegacyInputProfile.ScanCache) return Result(LegacyImportDecision.RescanRequired, "LegacyCacheRequiresRescan");
            if (record.Problem is not null) return Result(record.Problem.StartsWith("Unrecognized", StringComparison.Ordinal) || profile == LegacyInputProfile.Unknown ? LegacyImportDecision.Unsupported : LegacyImportDecision.Rejected, record.Problem);
            if (profile is LegacyInputProfile.Glossary or LegacyInputProfile.KeyMapping or LegacyInputProfile.AgentConfig or LegacyInputProfile.Questionnaire or LegacyInputProfile.NotesText) return Result(LegacyImportDecision.Retained, "RawUserDataRetained");
            if (profile == LegacyInputProfile.ReplyArray) return Result(LegacyImportDecision.RescanRequired, "ReplyHasNoVerifiedLocation");
            if (!acknowledged || !manifest.CoverageComplete || !manifest.Scope.Dimensions.IsEmpty || !manifest.Scope.Regions.IsEmpty || !manifest.Scope.Types.IsEmpty || !manifest.Scope.ArtifactPaths.IsEmpty)
                return Result(LegacyImportDecision.RescanRequired, "CompleteTargetAndExplicitRebindRequired");
            if (!LegacySlotMapping.TryBossbar(record, out var path, out var legacyContext)) return Result(LegacyImportDecision.RescanRequired, "LegacyLocationTypeContextOrShapeMissing");
            var candidates = locations[path].ToArray();
            retainedCandidates = checked(retainedCandidates + candidates.Length);
            if (retainedCandidates > 4096) throw new IOException("Legacy report exceeds aggregate candidate budget");
            var addresses = candidates.Select(row => row.Text.Address).ToImmutableArray();
            if (candidates.Length == 0) return Result(LegacyImportDecision.RescanRequired, "LegacyLocationMissing");
            if (candidates.Length != 1) return Result(LegacyImportDecision.Conflict, "AmbiguousLegacySlotShape", addresses);
            var candidate = candidates[0]; var slot = candidate.Text.ToSlot(sourceLocale, targetLocale);
            if (candidate.Hash is null || candidate.Text.Address.SemanticRole != "bossbar.name" || record.Context != legacyContext || candidate.Text.Text != record.Original || record.SourceHash is not null && record.SourceHash != candidate.Hash)
                return Result(LegacyImportDecision.Conflict, "LegacySourceEvidenceChanged", addresses, candidate.Hash);
            if (record.Translation is not null && !LegacySlotMapping.Validate(slot, record.Translation, manifest.SnapshotId.Digest)) return Result(LegacyImportDecision.Rejected, "LegacyTranslationValidationFailed", addresses, candidate.Hash);
            return Result(LegacyImportDecision.Mapped, record.SourceHash is null ? "UniqueReconstructedWithoutHistoricalHash" : "UniqueReconstructedWithSourceHash", addresses, candidate.Hash, slot.Id.Digest);
        }
    }
}
