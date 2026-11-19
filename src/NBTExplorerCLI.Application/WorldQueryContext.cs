using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed class WorldQueryContext
{
    internal WorldTextExtraction Extraction { get; }
    internal ScanExecutionOptions Execution { get; }
    internal ImmutableDictionary<string, ImmutableArray<DiscoveredSource>> Groups { get; }
    internal ImmutableDictionary<string, Hash256> ParseKeys { get; }
    internal ImmutableArray<ManifestSource> Inputs { get; }
    internal InterpretationVersions Versions { get; }
    internal StableIdentity SnapshotId { get; }
    internal WorldQueryContext(IWorldInventory inventory, WorldQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Scan);
        if (!Enum.IsDefined(request.Purpose)) throw new ArgumentOutOfRangeException(nameof(request));
        Execution = request.Execution ?? new(); Execution.Validate();
        Extraction = new(inventory, request.Scan, request.StorageRules, cancellationToken);
        if (MinecraftProfiles.IsReadOnlyAnvil(Extraction.LayoutProfile) && request.Purpose is WorldQueryPurpose.Session or WorldQueryPurpose.Convert)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Early versioned Anvil supports inspection and extraction only");
        if (Extraction.LayoutProfile == MinecraftProfiles.ClassicAnvil && request.Purpose == WorldQueryPurpose.Session
            || Extraction.Discovery.Metadata?.Profile == MinecraftProfiles.ClassicAnvil && request.Purpose == WorldQueryPurpose.Convert)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Classic Anvil supports inspection and extraction only");
        Groups = Extraction.ScanSources
            .GroupBy(source => source.Location.Artifact.RelativePath, StringComparer.Ordinal).ToImmutableDictionary(group => group.Key, group => group.ToImmutableArray(), StringComparer.Ordinal);
        Inputs = Groups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Input(pair.Value)).ToImmutableArray();
        var policyRules = Extraction.StorageRules.SelectMany(pair => pair.Value.Select(rule => SourceIdentity.Occurrence(new(pair.Key, rule.Path, "policy.storage." + rule.Kind)).Digest.Hex)).Order(StringComparer.Ordinal).ToArray();
        var policy = Hash256.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { limits = Extraction.Limits, request.Scan.MaximumSources, Execution.InFlightBytes, storageRules = policyRules })));
        var layout = Extraction.LayoutProfile?.Id;
        var extractor = layout == MinecraftProfiles.ClassicAnvil.Id ? "world-text-classic-anvil-3" : "world-text-command-28";
        var baseline = new InterpretationVersions("nbt-snbt-json-zip-1", MinecraftProfiles.IsVersionedClassic(Extraction.LayoutProfile) ? layout! : "minecraft-known-3", extractor, "scan-2." + policy.Hex);
        static IEnumerable<CacheSourceBinding> Bindings(IEnumerable<DiscoveredSource> sources) => sources.Select(source => new CacheSourceBinding(source.Location, source.Chunk, source.PackMetadataHash));
        Versions = baseline with { Policy = baseline.Policy + "." + ScanCacheJson.BindingHash(layout, Bindings(Groups.Values.SelectMany(group => group))).Hex };
        SnapshotId = SourceIdentity.Snapshot(inventory.WorldId, Inputs, request.Scan.Scope, Versions);
        var types = new SnapshotScope([], [], request.Scan.Scope.Types, []);
        ParseKeys = Groups.ToImmutableDictionary(pair => pair.Key, pair => SourceIdentity.Snapshot(inventory.WorldId, [Input(pair.Value)], types,
            baseline with { Policy = baseline.Policy + "." + ScanCacheJson.BindingHash(layout, Bindings(pair.Value)).Hex }).Digest, StringComparer.Ordinal);
    }
    private static ManifestSource Input(ImmutableArray<DiscoveredSource> sources)
    {
        var root = sources.Single(source => source.Location.Artifact.ContainerEntry is null && source.Chunk is null);
        return new(root.Location.Artifact, root.ContentHash, root.Outcome, root.DiagnosticCode);
    }
}
