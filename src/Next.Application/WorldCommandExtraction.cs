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
        long retained = 0;
        void Charge(SourceAddress address, string value)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, value.Length));
            if (retained > (request.Limits ?? DecodeLimits.Default).MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined source extraction exceeds retention budget");
        }
        foreach (var item in text.Occurrences) Charge(item.Address, item.Text);
        foreach (var key in text.TranslationKeys) Charge(key.Address, key.Key);
        foreach (var diagnostic in text.Diagnostics) Charge(diagnostic.Address, diagnostic.Code);
        foreach (var candidate in text.Unclassified) Charge(candidate.Address, candidate.Text);
        foreach (var reference in references) Charge(reference.Address, reference.Name);
        return retained;
    }
}
