using System.Collections.Immutable;
using Next.Domain;
using Next.Minecraft;

namespace Next.Application;

public sealed partial class WorldTextExtraction
{
    private CommandExtractionResult ExtractCommands(ImmutableArray<CommandCandidate> pending, CapabilityProfile profile)
    {
        var texts = ImmutableArray.CreateBuilder<TextOccurrence>(); var keys = ImmutableArray.CreateBuilder<TranslationKeyReference>();
        var diagnostics = ImmutableArray.CreateBuilder<ExtractionDiagnostic>(); var references = ImmutableArray.CreateBuilder<NameReferenceCandidate>();
        var completeness = ReferenceCompleteness.Complete; long retained = 0;
        foreach (var candidate in pending)
        {
            var result = CommandTextExtractor.Extract(candidate, profile, request.Limits, request.Scope);
            retained = checked(retained + CheckRetention(result.Text, result.References));
            if (retained > (request.Limits ?? DecodeLimits.Default).MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined commands exceed retention budget");
            texts.AddRange(result.Text.Occurrences); keys.AddRange(result.Text.TranslationKeys); diagnostics.AddRange(result.Text.Diagnostics); references.AddRange(result.References);
            if (result.ReferenceCompleteness != ReferenceCompleteness.Complete) completeness = ReferenceCompleteness.Unresolved;
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
