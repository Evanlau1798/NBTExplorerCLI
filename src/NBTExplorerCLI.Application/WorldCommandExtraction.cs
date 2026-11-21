using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldTextExtraction
{
    private CommandExtractionResult ExtractCommands(ImmutableArray<CommandCandidate> pending, CapabilityProfile profile, Action<CommandStorageAccess>? storageAccess,
        StorageDisplayPolicy? previewStorage, Action? opaqueEquipment, Action<CommandFunctionCall>? functionCall,
        Action<SourceAddress, CommandSignEffect>? signEffect)
    {
        var texts = ImmutableArray.CreateBuilder<TextOccurrence>(); var keys = ImmutableArray.CreateBuilder<TranslationKeyReference>();
        var diagnostics = ImmutableArray.CreateBuilder<ExtractionDiagnostic>(); var references = ImmutableArray.CreateBuilder<NameReferenceCandidate>();
        var completeness = ReferenceCompleteness.Complete; long retained = 0;
        foreach (var candidate in pending)
        {
            var policy = previewStorage ?? (storageAccess is null && candidate.Address.SemanticRole == "command.function" ? StorageDisplays : null);
            var result = CommandTextExtractor.Extract(candidate, profile, request.Limits, request.Scope, null, null, null, storageAccess,
                storageDisplayLists: policy?.Lists, runtimeDataVersion: Discovery.Metadata?.DataVersion, opaqueEquipment: opaqueEquipment,
                functionCall: functionCall, signEffect: signEffect);
            retained = checked(retained + CheckRetention(result.Text, result.References));
            if (retained > (request.Limits ?? DecodeLimits.Default).MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined commands exceed retention budget");
            texts.AddRange(result.Text.Occurrences); keys.AddRange(result.Text.TranslationKeys); references.AddRange(result.References);
            var remaining = policy?.Remaining(result.Text.Diagnostics) ?? result.Text.Diagnostics;
            diagnostics.AddRange(remaining);
            if (!StorageDisplayPolicy.ReferencesComplete(result, remaining))
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
