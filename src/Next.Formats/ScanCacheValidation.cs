using Next.Domain;
using System.Text.Json;

namespace Next.Formats;

internal static class ScanCacheValidation
{
    internal static void Shard(ArtifactCacheShard shard)
    {
        if (shard is null || shard.ParseKey is null || shard.Sources.IsDefault || shard.Sources.Length is < 1 or > 100_000) Invalid();
        var keys = new HashSet<(ArtifactAddress, WorldSourceRole, ChunkPosition?)>(); var paths = new HashSet<string>(StringComparer.Ordinal); var worlds = new HashSet<Guid>(); long retained = 0;
        foreach (var source in shard!.Sources)
        {
            if (source is null || source.Artifact is null || !Enum.IsDefined(source.Role) || !Enum.IsDefined(source.Outcome) || source.Outcome == SourceOutcome.Cancelled
                || !keys.Add((source.Artifact, source.Role, source.Chunk))) Invalid();
            paths.Add(source!.Artifact.RelativePath); worlds.Add(source.Artifact.WorldId);
            if (source.Text is not null) Text(source.Text, source);
            if (source.Commands is not null)
            {
                Text(source.Commands.Text, source);
                if (source.Commands.References.IsDefault || source.Commands.References.Any(reference => reference is null || reference.Name is null || !Bound(reference.Address, source)) || !Enum.IsDefined(source.Commands.ReferenceCompleteness)) Invalid();
            }
            var retainedText = source.Text ?? source.Commands?.Text;
            if (retainedText is not null) retained = checked(retained + TextExtractionBudget.ResultBytes(retainedText, source.Commands?.References));
            if (retained > DecodeLimits.Default.MaxDecodedBytes || source.Outcome == SourceOutcome.Success && (source.Text is null || !source.Text.Complete || source.Commands is { Complete: false })
                || source.Outcome == SourceOutcome.Irrelevant && (source.Text is { } irrelevant && (!irrelevant.Complete || !irrelevant.Occurrences.IsEmpty || !irrelevant.TranslationKeys.IsEmpty)
                    || source.Commands is { } commands && (!commands.Complete || !commands.References.IsEmpty || !commands.Text.Occurrences.IsEmpty || !commands.Text.TranslationKeys.IsEmpty))
                || source.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported && string.IsNullOrWhiteSpace(source.DiagnosticCode)) Invalid();
        }
        if (paths.Count != 1 || worlds.Count != 1) Invalid();
    }
    private static void Text(TextExtractionResult? text, CachedSource source)
    {
        if (text is null || text.Occurrences.IsDefault || text.TranslationKeys.IsDefault || text.Diagnostics.IsDefault || text.PendingCommands.IsDefault || text.Unclassified.IsDefault) Invalid();
        if (text!.Occurrences.Any(item => item is null || item.Text is null || item.Context is null || !Bound(item.Address, source))
            || text.TranslationKeys.Any(item => item is null || item.Key is null || !Bound(item.Address, source))
            || text.Diagnostics.Any(item => item is null || string.IsNullOrWhiteSpace(item.Code) || !Bound(item.Address, source))
            || text.PendingCommands.Any(item => item is null || item.Command is null || !Bound(item.Address, source))
            || text.Unclassified.Any(item => item is null || item.Text is null || !Bound(item.Address, source))) Invalid();
    }
    private static bool Bound(SourceAddress? address, CachedSource source) => address is not null && address.Artifact == source.Artifact && address.Chunk == source.Chunk;
    internal static void Manifest(ScanCacheManifest manifest)
    {
        if (manifest is null || manifest.WorldId == Guid.Empty || manifest.Scope is null || manifest.Versions is null || manifest.SnapshotId is null
            || manifest.Artifacts.IsDefault || manifest.InputSources.IsDefault || manifest.Artifacts.Length > 100_000 || manifest.InputSources.Length > 100_000
            || new[] { manifest.Versions.Codec, manifest.Versions.Profile, manifest.Versions.Rules, manifest.Versions.Policy }.Any(string.IsNullOrWhiteSpace)) Invalid();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in manifest!.Artifacts)
        {
            if (artifact is null || artifact.ParseKey is null || artifact.ContentHash is null || artifact.SourceCount is < 1 or > 100_000 || artifact.TextCount < 0 || artifact.ReferenceCount < 0 || !paths.Add(artifact.RelativePath)) Invalid();
            try { ArtifactAddress.ValidatePath(artifact!.RelativePath, false); } catch (ArgumentException) { Invalid(); }
        }
        if (manifest.InputSources.Any(source => source is null || source.Artifact is null || source.Artifact.WorldId != manifest.WorldId || source.Artifact.ContainerEntry is not null)
            || manifest.InputSources.Length != paths.Count || manifest.InputSources.Select(source => source.Artifact.RelativePath).Distinct(StringComparer.Ordinal).Count() != paths.Count
            || manifest.InputSources.Any(source => !paths.Contains(source.Artifact.RelativePath))) Invalid();
        StableIdentity expected;
        try { expected = SourceIdentity.Snapshot(manifest.WorldId, manifest.InputSources, manifest.Scope, manifest.Versions); }
        catch (ArgumentException) { Invalid(); throw; }
        if (manifest.SnapshotId != expected || manifest.CoverageComplete != (manifest.CatalogDiagnosticCode is null && manifest.Artifacts.All(artifact => artifact.CoverageComplete)
            && manifest.InputSources.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant))) Invalid();
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new JsonException("Invalid cache contract");
}
