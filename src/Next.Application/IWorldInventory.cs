using Next.Domain;

namespace Next.Application;

public interface IWorldInventory
{
    Guid WorldId { get; }
    Hash256? AccessBinding => null;
    WorldInventorySnapshot Enumerate(int maxEntries = 100_000);
    byte[] Read(ArtifactAddress artifact);
}
