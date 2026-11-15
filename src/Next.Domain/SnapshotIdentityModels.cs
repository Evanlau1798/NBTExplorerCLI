using System.Collections.Immutable;

namespace Next.Domain;

public enum SourceOutcome { Success, Irrelevant, Unsupported, Failed, Cancelled }
public sealed record ManifestSource(ArtifactAddress Artifact, Hash256? ContentHash, SourceOutcome Outcome, string? DiagnosticCode = null);
public sealed record InterpretationVersions(string Codec, string Profile, string Rules, string Policy);

public sealed class SnapshotScope
{
    public ImmutableArray<string> Dimensions { get; }
    public ImmutableArray<string> Regions { get; }
    public ImmutableArray<string> Types { get; }
    public ImmutableArray<string> ArtifactPaths { get; }
    public SnapshotScope(IEnumerable<string> dimensions, IEnumerable<string> regions, IEnumerable<string> types,
        IEnumerable<string> artifactPaths)
        => (Dimensions, Regions, Types, ArtifactPaths) = (Freeze(dimensions), Freeze(regions), Freeze(types), Freeze(artifactPaths));
    public static SnapshotScope All { get; } = new([], [], [], []);
    private static ImmutableArray<string> Freeze(IEnumerable<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = source.ToImmutableArray();
        if (values.IsDefault || values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid scope filter", nameof(source));
        return values;
    }
}
