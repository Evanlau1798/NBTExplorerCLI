using Next.Domain;

namespace Next.Application;

public interface IWorldInventory
{
    Guid WorldId { get; }
    WorldInventorySnapshot Enumerate(int maxEntries = 100_000);
    byte[] Read(ArtifactAddress artifact);
}
