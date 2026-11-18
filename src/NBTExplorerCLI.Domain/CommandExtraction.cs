using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public sealed record NameReferenceCandidate(SourceAddress Address, string Name, bool Negated);
public sealed record CommandExtractionResult(TextExtractionResult Text, ImmutableArray<NameReferenceCandidate> References, ReferenceCompleteness ReferenceCompleteness)
{
    public bool Complete => Text.Complete && ReferenceCompleteness == ReferenceCompleteness.Complete;
}
