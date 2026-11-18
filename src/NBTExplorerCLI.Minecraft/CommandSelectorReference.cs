using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Minecraft;

public sealed record CommandSelectorReference(NameReferenceCandidate Reference, string Selector, char Kind, string? EntityType, bool Direct);
