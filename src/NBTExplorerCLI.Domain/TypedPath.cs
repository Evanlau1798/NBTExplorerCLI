using System.Collections.Immutable;
using System.Globalization;

namespace NBTExplorerCLI.Domain;

public enum PathSegmentKind { Key, Index }

public sealed record PathSegment
{
    public PathSegmentKind Kind { get; }
    public string? Key { get; }
    public int? Index { get; }
    private PathSegment(PathSegmentKind kind, string? key, int? index) => (Kind, Key, Index) = (kind, key, index);
    public static PathSegment ForKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(PathSegmentKind.Key, key, null);
    }
    public static PathSegment ForIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(PathSegmentKind.Index, null, index);
    }
}

public sealed class TypedPath : IEquatable<TypedPath>
{
    public static TypedPath Root { get; } = new([]);
    public ImmutableArray<PathSegment> Segments { get; }
    public TypedPath(IEnumerable<PathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.ToImmutableArray();
        if (Segments.Length > 256) throw new ArgumentException("Typed path exceeds depth 256", nameof(segments));
        if (Segments.Any(segment => segment is null)) throw new ArgumentException("Path segment cannot be null", nameof(segments));
    }
    public string ToJsonPointer() => string.Concat(Segments.Select(segment => "/" + (segment.Kind == PathSegmentKind.Key
        ? segment.Key!.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)
        : segment.Index!.Value.ToString(CultureInfo.InvariantCulture))));
    public bool Equals(TypedPath? other) => other is not null && Segments.SequenceEqual(other.Segments);
    public override bool Equals(object? obj) => obj is TypedPath other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Segments) hash.Add(segment);
        return hash.ToHashCode();
    }
}
