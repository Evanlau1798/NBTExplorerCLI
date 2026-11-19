using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldTextExtraction
{
    private CommandExtractionResult ExtractCommands(ImmutableArray<CommandCandidate> pending, CapabilityProfile profile, Action<CommandStorageAccess>? storageAccess,
        StorageDisplayPolicy? previewStorage)
    {
        var texts = ImmutableArray.CreateBuilder<TextOccurrence>(); var keys = ImmutableArray.CreateBuilder<TranslationKeyReference>();
        var diagnostics = ImmutableArray.CreateBuilder<ExtractionDiagnostic>(); var references = ImmutableArray.CreateBuilder<NameReferenceCandidate>();
        var completeness = ReferenceCompleteness.Complete; long retained = 0;
        foreach (var candidate in pending)
        {
            var policy = previewStorage ?? (storageAccess is null && candidate.Address.SemanticRole == "command.function" ? StorageDisplays : null);
            var result = CommandTextExtractor.Extract(candidate, profile, request.Limits, request.Scope, storageAccess: storageAccess, storageDisplayLists: policy?.Lists);
            retained = checked(retained + CheckRetention(result.Text, result.References));
            if (retained > (request.Limits ?? DecodeLimits.Default).MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined commands exceed retention budget");
            texts.AddRange(result.Text.Occurrences); keys.AddRange(result.Text.TranslationKeys); references.AddRange(result.References);
            var remaining = policy is { CanBindWrites: true } && !result.Text.Diagnostics.IsEmpty
                ? result.Text.Diagnostics.Where(diagnostic => !policy.Covers(diagnostic)).ToImmutableArray() : result.Text.Diagnostics;
            diagnostics.AddRange(remaining);
            if (result.ReferenceCompleteness != ReferenceCompleteness.Complete && (result.Text.Diagnostics.IsEmpty || !remaining.IsEmpty))
                completeness = ReferenceCompleteness.Unresolved;
        }
        return new(new(texts.ToImmutable(), keys.ToImmutable(), diagnostics.ToImmutable(), [], []), references.ToImmutable(), completeness);
    }
    private long CheckRetention(TextExtractionResult text, ImmutableArray<NameReferenceCandidate> references)
    {
        var retained = TextExtractionBudget.ResultBytes(text, references);
        if (retained > (request.Limits ?? DecodeLimits.Default).MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined source extraction exceeds retention budget");
        return retained;
    }
}
