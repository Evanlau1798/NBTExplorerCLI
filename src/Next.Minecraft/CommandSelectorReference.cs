using Next.Domain;

namespace Next.Minecraft;

public sealed record CommandSelectorReference(NameReferenceCandidate Reference, string Selector, char Kind, string? EntityType, bool Direct);
