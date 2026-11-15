using System.Collections.Immutable;

namespace Next.Domain;

public enum SupportLevel { Unknown, Supported, Unsupported }
public enum ProfileOperation { Read, Extract, Plan, Write, Recover }
public enum ProfileFacet { Layout, ItemSchema, TextSchema, CommandGrammar, Compression }
public enum LogicalListEncoding { Unknown, Homogeneous, CompoundWrapper }

public sealed record OperationSupport(SupportLevel Read = SupportLevel.Unknown, SupportLevel Extract = SupportLevel.Unknown,
    SupportLevel Plan = SupportLevel.Unknown, SupportLevel Write = SupportLevel.Unknown, SupportLevel Recover = SupportLevel.Unknown)
{
    public SupportLevel For(ProfileOperation operation) => operation switch
    {
        ProfileOperation.Read => Read,
        ProfileOperation.Extract => Extract,
        ProfileOperation.Plan => Plan,
        ProfileOperation.Write => Write,
        ProfileOperation.Recover => Recover,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}

public sealed record FormatFeature
{
    public string Schema { get; }
    public int Version { get; }
    public OperationSupport Operations { get; }
    public FormatFeature(string schema, int version, OperationSupport operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(operations);
        if (Enum.GetValues<ProfileOperation>().Any(operation => !Enum.IsDefined(operations.For(operation))))
            throw new ArgumentException("Invalid operation support", nameof(operations));
        (Schema, Version, Operations) = (schema, version, operations);
    }
}

public sealed record ListMapping
{
    public LogicalListEncoding Encoding { get; }
    public string? WrapperKey { get; }
    public ListMapping(LogicalListEncoding encoding, string? wrapperKey = null)
    {
        if (!Enum.IsDefined(encoding) || (encoding == LogicalListEncoding.CompoundWrapper) != (wrapperKey is not null))
            throw new ArgumentException("Only explicit compound wrapping requires a literal wrapper key", nameof(wrapperKey));
        (Encoding, WrapperKey) = (encoding, wrapperKey);
    }
}

public sealed class CapabilityProfile
{
    public string Id { get; }
    public int Version { get; }
    public ImmutableDictionary<ProfileFacet, FormatFeature> Features { get; }
    public ListMapping Lists { get; }
    public CapabilityProfile(string id, int version, IEnumerable<KeyValuePair<ProfileFacet, FormatFeature>> features,
        ListMapping lists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(lists);
        Features = features.ToImmutableDictionary();
        if (Features.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null))
            throw new ArgumentException("Invalid profile facet", nameof(features));
        (Id, Version, Lists) = (id, version, lists);
    }
    public bool Supports(ProfileOperation operation, params ProfileFacet[] requiredFacets)
    {
        ArgumentNullException.ThrowIfNull(requiredFacets);
        if (!Enum.IsDefined(operation) || requiredFacets.Length == 0 || requiredFacets.Any(facet => !Enum.IsDefined(facet)))
            throw new ArgumentException("Operation and at least one known facet are required");
        return requiredFacets.All(facet => Features.TryGetValue(facet, out var feature)
            && feature.Operations.For(operation) == SupportLevel.Supported);
    }
}

public sealed record LogicalNbtList
{
    public ImmutableArray<NbtValue> Items { get; }
    public LogicalNbtList(IEnumerable<NbtValue> items)
    {
        Items = NbtSequence.Copy(items);
        if (Items.Any(item => item is null)) throw new ArgumentException("Logical list cannot contain null", nameof(items));
    }
    public bool Equals(LogicalNbtList? other) => other is not null && Items.SequenceEqual(other.Items);
    public override int GetHashCode() => NbtSequence.Hash(NbtType.List, Items);
}

public static class DataSchemas
{
    public const string TypedNbt = "nbtcli.next.typed-nbt";
    public const string DisplayJson = "nbtcli.next.display-json";
    public const string MinecraftText = "nbtcli.next.minecraft-text";
    public const int Version = 1;
}
