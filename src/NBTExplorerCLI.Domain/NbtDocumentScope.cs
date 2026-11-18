namespace NBTExplorerCLI.Domain;

internal static class NbtDocumentScope
{
    public static void RequireDocument(SourceAddress document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.NodePath.Segments.Length != 0 || !IsPhysical(document))
            throw new EditRejectedException(EditFailure.InvalidPath, "Requires an exact physical NBT document binding");
    }
    public static void RequireTarget(SourceAddress document, SourceAddress address)
    {
        if (!IsPhysical(document) || !IsPhysical(address) || document.Artifact != address.Artifact
            || document.Dimension != address.Dimension || document.Chunk != address.Chunk)
            throw new EditRejectedException(EditFailure.InvalidPath, "Target does not belong to this physical NBT document");
    }
    private static bool IsPhysical(SourceAddress address) => address.ComponentPath is null && address.Syntax is null
        && address.JsonTarget == JsonTargetKind.Value && address.Artifact.Kind is ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities;
}
