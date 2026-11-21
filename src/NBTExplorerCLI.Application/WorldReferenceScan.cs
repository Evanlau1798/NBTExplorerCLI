using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace NBTExplorerCLI.Application;

internal sealed record WorldNameUse(SourceAddress Address, string Name, char Kind, string? Type, string Selector, bool Direct, bool Writable, FormatFeature Grammar);
internal sealed class WorldReferenceScan
{
    internal List<WorldNamedTarget> Targets { get; } = [];
    internal List<WorldNameUse> Uses { get; } = [];
    internal ImmutableArray<ArtifactPrecondition> Inputs { get; }
    private readonly DecodeLimits limits;
    private readonly HashSet<SourceAddress> targetRoots = [];
    private long retained;
    internal static bool Identifying(string role) => role is "entity.name" or "armor_stand.name";
    internal static void Reject(string reason) => throw new EditRejectedException(EditFailure.Precondition, reason);
    internal static IEnumerable<DiscoveredSource> Relevant(WorldDiscoveryResult discovery) => discovery.Sources.Where(source => source.Outcome == SourceOutcome.Success);
    internal static void Match(WorldDiscoveryResult expected, WorldDiscoveryResult actual)
    {
        if (!actual.Complete) Reject("Reference discovery is incomplete");
        static object Binding(DiscoveredSource source) => (source.Location, source.Outcome, source.ContentHash, source.PackMetadataHash);
        var before = Relevant(expected).ToDictionary(source => (source.Location.Artifact, source.Chunk), Binding);
        var after = Relevant(actual).ToDictionary(source => (source.Location.Artifact, source.Chunk), Binding);
        if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var value) || !Equals(value, pair.Value))
            || !(expected.MetadataSources.IsDefault ? [] : expected.MetadataSources).SequenceEqual(actual.MetadataSources.IsDefault ? [] : actual.MetadataSources))
            throw new ArtifactConflictException("The complete reference source set changed after scanning");
    }
    internal WorldReferenceScan(IWorldInventory inventory, WorldQuerySnapshot snapshot)
    {
        limits = snapshot.Limits; var scope = snapshot.Manifest.Scope;
        if (snapshot.Manifest.WorldId != inventory.WorldId || !snapshot.Manifest.CoverageComplete || !scope.Dimensions.IsEmpty || !scope.Regions.IsEmpty || !scope.Types.IsEmpty || !scope.ArtifactPaths.IsEmpty)
            Reject("Reference synchronization requires a complete unfiltered world snapshot");
        var request = new ScanRequest(SnapshotScope.All, limits);
        var discovery = WorldDiscovery.Discover(inventory, request); Match(snapshot.Discovery, discovery);
        Inputs = Relevant(discovery).Where(source => source.Chunk is null && source.Location.Artifact.ContainerEntry is null)
            .Select(source => new ArtifactPrecondition(source.Location.Artifact, ArtifactImage.Present(source.ContentHash!)))
            .Concat((discovery.MetadataSources.IsDefault ? [] : discovery.MetadataSources).Select(source => new ArtifactPrecondition(source.Artifact, ArtifactImage.Present(source.ContentHash!))))
            .Distinct().ToImmutableArray();
        foreach (var group in Relevant(discovery).GroupBy(source => source.Location.Artifact.RelativePath, StringComparer.Ordinal))
        {
            var outer = group.Single(source => source.Chunk is null && source.Location.Artifact.ContainerEntry is null);
            var bytes = inventory.Read(outer.Location.Artifact);
            if (Hash256.Compute(bytes) != outer.ContentHash) throw new ArtifactConflictException("Reference artifact changed while reading");
            using var archive = outer.Location.Artifact.Kind == ArtifactKind.Archive ? ZipArchiveValidation.OpenRead(bytes, limits) : null;
            var region = outer.Location.Artifact.Kind is ArtifactKind.Region or ArtifactKind.Entities ? RegionFile.Read(bytes) : null;
            foreach (var source in group)
            {
                if (source.Location.Role == WorldSourceRole.Archive || region is not null && source.Chunk is null) continue;
                var payload = bytes;
                if (archive is not null)
                {
                    var entry = archive.GetEntry(source.Location.Artifact.ContainerEntry!) ?? throw new ArtifactConflictException("Reference ZIP member disappeared");
                    payload = new byte[checked((int)entry.Length)]; using var stream = entry.Open(); stream.ReadExactly(payload);
                    if (Hash256.Compute(payload) != source.ContentHash) throw new ArtifactConflictException("Reference ZIP member changed");
                }
                NbtDocument? document = null;
                if (region is not null && source.Chunk is { } chunk) document = region.ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, limits).Document;
                else if (source.Location.Artifact.Kind == ArtifactKind.Nbt || source.Location.Role == WorldSourceRole.Structure) document = WorldArtifactEdits.ReadNbt(payload, limits).Document;
                Read(source, payload, document, discovery.Metadata!, snapshot);
            }
        }
        Match(discovery, WorldDiscovery.Discover(inventory, request));
    }
    private void Charge(SourceAddress address, string value)
    {
        retained = checked(retained + TextExtractionBudget.RecordBytes(address, value.Length));
        if (retained > limits.MaxDecodedBytes || Targets.Count + Uses.Count > 100_000) throw new CodecException(CodecError.LimitExceeded, 0, "World reference graph exceeds retention budget");
    }
    private void Read(DiscoveredSource source, byte[] bytes, NbtDocument? document, WorldMetadata metadata, WorldQuerySnapshot snapshot)
    {
        var roots = new List<SourceAddress>(); TextExtractionResult text;
        var profile = document is not null ? NbtTextExtractor.Profile(source.Location, document) : source.Pack is { } pack ? MinecraftProfiles.ForPackMetadata(pack) : metadata.Profile;
        if (profile is null) { Reject("Unknown reference grammar profile"); return; }
        if (document is not null)
        {
            var artifact = source.Location.Artifact;
            var rules = snapshot.StorageRules.GetValueOrDefault(artifact, []);
            var storage = rules.IsEmpty && snapshot.StorageDisplays.CanBindWrites ? snapshot.StorageDisplays : StorageDisplayPolicy.Empty;
            if (rules.IsEmpty) rules = storage.SeedRules.GetValueOrDefault(artifact, []);
            text = StorageDisplayPolicy.RetainSeeds(NbtTextExtractor.Extract(source.Location, document, source.Chunk, limits,
                rules, layoutProfile: metadata.Profile, componentRoot: roots.Add), storage.RetainedSeeds.GetValueOrDefault(artifact));
        }
        else if (source.Location.Role == WorldSourceRole.Function)
            text = new([], [], [], [WorldCommandEdits.BindFunction(source.Location.Artifact, bytes, profile, source.Location.Dimension)], []);
        else if (source.Pack is { } sourcePack) text = PackTextExtractor.Extract(source.Location, bytes, sourcePack, limits, componentRoot: roots.Add);
        else text = DataTextExtractor.Json(source.Location, bytes, metadata, limits, componentRoot: roots.Add);
        if (!text.Diagnostics.IsEmpty || !text.Unclassified.IsEmpty) Reject("Reference source has unknown or invalid content");
        foreach (var command in text.PendingCommands)
        {
            var grammar = profile.Features[ProfileFacet.CommandGrammar];
            var declared = new HashSet<SourceAddress>();
            var storage = command.Address.SemanticRole == "command.function" && snapshot.StorageDisplays.CanBindWrites
                ? snapshot.StorageDisplays : StorageDisplayPolicy.Empty;
            var result = CommandTextExtractor.Extract(command, profile, limits, selectorReference: binding =>
            {
                var reference = binding.Reference; Charge(reference.Address, binding.Selector);
                Uses.Add(new(reference.Address, reference.Name, binding.Kind, binding.EntityType, binding.Selector, true, binding.Direct, grammar));
            }, nameTemplate: template =>
            {
                var target = new WorldNamedTarget(template.Root, template.EntityType, template.Name, template.Parts, grammar);
                if (!targetRoots.Add(target.Root)) Reject("An identifying summon declaration occurs more than once");
                Charge(target.Root, target.Name);
                foreach (var part in target.Parts)
                {
                    if (!declared.Add(part.Address)) Reject("A name part belongs to multiple summon declarations");
                    Charge(part.Address, part.Text);
                }
                Targets.Add(target);
            }, storageDisplayLists: storage.Lists, runtimeDataVersion: source.RuntimeDataVersion);
            var remaining = storage.Remaining(result.Text.Diagnostics);
            if (!remaining.IsEmpty || !StorageDisplayPolicy.ReferencesComplete(result, remaining)
                || result.Text.Occurrences.Any(item => Identifying(item.Address.SemanticRole) && !declared.Contains(item.Address)))
                Reject("Command reference domain is dynamic or contains unsupported named templates");
        }
        if (text.Occurrences.Any(item => Identifying(item.Address.SemanticRole) && (document is null || item.Address.Syntax is not null))) Reject("Named templates require their explicit reference grammar");
        if (document is not null)
        {
            foreach (var root in roots.Where(root => Identifying(root.SemanticRole))) AddTarget(root, document, profile);
            var handled = new HashSet<TypedPath>();
            foreach (var root in roots)
            {
                if (root.Syntax is not null) Reject("Encoded native reference grammar is unresolved");
                if (Covered(handled, root.NodePath)) continue;
                var value = NbtLocator.Resolve(document.Root, root.NodePath).Value ?? throw new ArtifactConflictException("Component root disappeared");
                var binding = new SourceAddress(root.Artifact, root.NodePath, root.SemanticRole, root.Dimension, root.Chunk);
                var references = root.ComponentPath is not null && value is NbtString encoded ? NativeComponentReferences.EmbeddedJson(binding, encoded.Value, profile, limits)
                    : NativeComponentReferences.Nbt(binding, value, profile, limits);
                if (!references.Diagnostics.IsEmpty && root.ComponentPath is not null && value is NbtString bookPage
                    && root.SemanticRole is "book.page" or "lectern.book.page" && snapshot.StorageDisplays.CanBindWrites)
                    references = ProveNativeBookReads(references, bookPage.Value, roots.Where(component => component.NodePath == root.NodePath).ToArray(), snapshot.StorageDisplays);
                Add(references, profile);
            }
        }
        else if (roots.Count != 0)
        {
            using var json = BoundedJson.ReadDecoded(bytes, limits); var handled = new HashSet<TypedPath>();
            foreach (var root in roots)
            {
                if (root.Syntax is not null || root.ComponentPath is not null) Reject("Encoded pack reference grammar is unresolved");
                if (Covered(handled, root.NodePath)) continue;
                var value = JsonValue(json.RootElement, root.NodePath);
                Add(NativeComponentReferences.Json(root, Encoding.UTF8.GetBytes(value.GetRawText()), profile, limits), profile);
            }
        }
    }
    private ComponentReferenceResult ProveNativeBookReads(ComponentReferenceResult result, string page,
        IReadOnlyList<SourceAddress> roots, StorageDisplayPolicy storage)
    {
        var authorized = new HashSet<TypedPath>();
        if (!NativeBookStorage.Observe(page, roots, limits, read =>
        {
            if (read.Use != CommandStorageUse.InterpretedDisplay || read.HasUnclassifiedReferences || read.Address.ComponentPath is not { } path) return;
            var member = read.Path.EndsWith("[]", StringComparison.Ordinal) ? read.Path[..^2] : read.Path;
            if (storage.Lists.Contains((read.Storage, member))) authorized.Add(path);
        }) || authorized.Count == 0) return result;
        using var json = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(page, limits), limits);
        return result with { Diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Code != "DynamicComponentReference"
            || diagnostic.Address.ComponentPath is not { } path || !authorized.Contains(path)
            || JsonValue(json.RootElement, path).TryGetNativeProperty("hoverEvent", out _)).ToImmutableArray() };
    }
    private static bool Covered(HashSet<TypedPath> roots, TypedPath path)
    {
        for (var length = 0; length <= path.Segments.Length; length++)
            if (roots.Contains(new TypedPath(path.Segments.Take(length)))) return true;
        roots.Add(path); return false;
    }
    private void AddTarget(SourceAddress address, NbtDocument document, CapabilityProfile profile)
    {
        var segments = address.NodePath.Segments; var index = segments.Length - 1;
        while (index >= 0 && (segments[index].Kind != PathSegmentKind.Key || segments[index].Key != "CustomName")) index--;
        if (index < 0) { Reject("An identifying name has no exact entity field root"); return; }
        var path = new TypedPath(segments.Take(index + 1));
        var root = new SourceAddress(address.Artifact, path, address.SemanticRole, address.Dimension, address.Chunk);
        if (!targetRoots.Add(root)) return;
        var idPath = new TypedPath(segments.Take(index).Append(PathSegment.ForKey("id")));
        if (NbtLocator.Resolve(document.Root, idPath).Value is not NbtString id) { Reject("Named entity identity is unresolved"); return; }
        var type = MinecraftIdentifier.DefaultNamespace(id.Value);
        if (type == "minecraft:player") Reject("Player identity is separate from entity CustomName");
        var target = WorldReferenceNames.Read(root, type, NbtLocator.Resolve(document.Root, path).Value!, profile, limits);
        Charge(root, target.Name); foreach (var part in target.Parts) Charge(part.Address, part.Text); Targets.Add(target);
    }
    private void Add(ComponentReferenceResult result, CapabilityProfile profile)
    {
        if (result.Completeness != ReferenceCompleteness.Complete) Reject("Native component reference domain is unresolved");
        foreach (var selector in result.Selectors)
            foreach (var name in selector.Parsed.Names)
            {
                Charge(selector.Address, selector.Selector);
                Uses.Add(new(selector.Address, name.Name, selector.Parsed.Kind, selector.Parsed.EntityType, selector.Selector, false, true, profile.Features[ProfileFacet.TextSchema]));
            }
    }
    private static JsonElement JsonValue(JsonElement value, TypedPath path)
    {
        foreach (var segment in path.Segments)
            if (segment.Kind == PathSegmentKind.Index) value = value[segment.Index!.Value];
            else if (value.TryGetNativeProperty(segment.Key!, out var child)) value = child;
            else throw new ArtifactConflictException("Native reference JSON root disappeared");
        return value;
    }
}
