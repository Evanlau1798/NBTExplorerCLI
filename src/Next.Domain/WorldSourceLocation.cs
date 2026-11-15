namespace Next.Domain;

public enum WorldSourceRole { Level, Player, SavedData, Terrain, Entities, PointOfInterest, Archive, PackMetadata, PackJson, Language, Function, Irrelevant, Unknown, PackRoot, Structure }
public enum PackSourceKind { Datapack, ResourcePack }
public sealed record WorldSourceLocation(ArtifactAddress Artifact, WorldSourceRole Role, string? Dimension = null,
    int? RegionX = null, int? RegionZ = null, string? PackRoot = null, PackSourceKind? PackKind = null);
