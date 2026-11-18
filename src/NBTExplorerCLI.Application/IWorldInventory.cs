using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

public interface IWorldInventory
{
    Guid WorldId { get; }
    Hash256? AccessBinding => null;
    WorldInventorySnapshot Enumerate(int maxEntries = 100_000);
    byte[] Read(ArtifactAddress artifact);
    ArtifactImage ReadImage(ArtifactAddress artifact)
    {
        try { return ArtifactImage.Present(Hash256.Compute(Read(artifact))); }
        catch (FileNotFoundException) { return ArtifactImage.Absent; }
    }
}
