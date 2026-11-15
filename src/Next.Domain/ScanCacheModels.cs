using System.Collections.Immutable;

namespace Next.Domain;

public enum CacheState { Fresh, Dirty, Missing, Corrupt, UnsupportedSchema }
public sealed record CacheInspection(CacheState State, string? Reason = null);
public sealed record CachedSource(ArtifactAddress Artifact, WorldSourceRole Role, ChunkPosition? Chunk, SourceOutcome Outcome,
    string? DiagnosticCode, TextExtractionResult? Text, CommandExtractionResult? Commands);
public sealed record ArtifactCacheShard(Hash256 ParseKey, ImmutableArray<CachedSource> Sources);
public sealed record CacheSourceBinding(WorldSourceLocation Location, ChunkPosition? Chunk, Hash256? MetadataHash);
public sealed record CachedArtifact(string RelativePath, Hash256 ParseKey, Hash256 ContentHash, int SourceCount, int TextCount, int ReferenceCount, bool CoverageComplete);
public sealed record ScanCacheManifest(Guid WorldId, SnapshotScope Scope, InterpretationVersions Versions, StableIdentity SnapshotId,
    ImmutableArray<CachedArtifact> Artifacts, ImmutableArray<ManifestSource> InputSources, bool CoverageComplete, string? CatalogDiagnosticCode = null);
