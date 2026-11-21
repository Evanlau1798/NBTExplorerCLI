using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed partial class WorldStorageUseIndex
{
    private readonly Dictionary<ArtifactAddress, HashSet<string>> unclassifiedSeeds = [];

    private static string? SavedStorageNamespace(WorldTextExtraction extraction, DiscoveredSource source)
        => source.Location is { Role: WorldSourceRole.SavedData, Artifact: { Kind: ArtifactKind.Nbt, ContainerEntry: null } artifact }
            && extraction.LayoutProfile is { } profile ? WorldLayoutLocator.CommandStorageNamespace(profile, artifact.RelativePath) : null;

    private void RecordUnclassifiedSeed(ArtifactAddress artifact, string space, IEnumerable<UnclassifiedTextCandidate> candidates)
    {
        if (!unclassifiedSeeds.TryGetValue(artifact, out var ids))
        {
            Charge(artifact.RelativePath.Length);
            unclassifiedSeeds.Add(artifact, ids = new(StringComparer.Ordinal));
        }
        foreach (var candidate in candidates)
        {
            if (SavedStorageLocalId(candidate.Address.NodePath) is not { } localId) continue;
            var id = space + ":" + localId;
            if (MinecraftIdentifier.ResourceLocation(id) && ids.Add(id)) Charge(id.Length);
        }
    }

    internal static string? SavedStorageLocalId(TypedPath path)
        => path.Segments is [{ Key: "data" }, { Key: "contents" }, { Key: { } localId }, _, ..] ? localId : null;

    private ImmutableDictionary<ArtifactAddress, ImmutableHashSet<string>> RetainUnreferencedSeeds(
        ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> seedRules)
    {
        var result = ImmutableDictionary.CreateBuilder<ArtifactAddress, ImmutableHashSet<string>>();
        foreach (var (artifact, ids) in unclassifiedSeeds)
        {
            var preserved = ids.Where(id => !uses.ContainsKey(id)).Select(id => id[(id.IndexOf(':') + 1)..])
                .Take(257).ToImmutableHashSet(StringComparer.Ordinal);
            if (preserved.Count != 0 && seedRules.GetValueOrDefault(artifact, []).Length + preserved.Count <= 256)
                result.Add(artifact, preserved);
        }
        return result.ToImmutable();
    }

    private static ImmutableDictionary<string, Hash256> WithSeedKeys(ImmutableDictionary<string, Hash256> producers,
        ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> seeds,
        ImmutableDictionary<ArtifactAddress, ImmutableHashSet<string>>? retained = null)
    {
        var rules = seeds.SelectMany(pair => pair.Value.Select(rule => (Artifact: pair.Key,
            Key: SourceIdentity.Occurrence(new(pair.Key, rule.Path, "policy.storage." + rule.Kind)).Digest.Hex)));
        var preserved = retained?.SelectMany(pair => pair.Value.Select(id => (Artifact: pair.Key,
            Key: SourceIdentity.Occurrence(new(pair.Key, new(new[] { "data", "contents", id }.Select(PathSegment.ForKey)),
                "policy.storage.retained")).Digest.Hex))) ?? [];
        return producers.SetItems(rules.Concat(preserved).GroupBy(item => item.Artifact)
            .Select(group => new KeyValuePair<string, Hash256>(group.Key.RelativePath,
                Hash256.Compute(Encoding.UTF8.GetBytes(string.Concat(group.Select(item => item.Key).Order(StringComparer.Ordinal)))))));
    }

    private IEnumerable<ListCandidate> ReadCompatibleLists(IWorldInventory inventory, WorldTextExtraction extraction)
    {
        var discovery = extraction.Discovery;
        if (!CompleteScope(discovery) || discovery.Metadata is not { } metadata) yield break;
        // Windows folds trailing dots in namespace directories, making even a separately named reader ambiguous.
        if (NamespacedData(metadata.Profile) && uses.Keys.Any(storage => storage[..storage.IndexOf(':')].EndsWith('.'))) yield break;
        var saved = new Dictionary<string, DiscoveredSource>(StringComparer.Ordinal);
        foreach (var source in discovery.Sources)
            if (source.Location is { Role: WorldSourceRole.SavedData, Artifact: { Kind: ArtifactKind.Nbt, ContainerEntry: null } artifact }
                && source.Chunk is null && WorldLayoutLocator.CommandStorageNamespace(metadata.Profile, artifact.RelativePath) is { } space)
                saved.Add(space, source);
        var matching = uses.Where(pair => !pair.Value.Ambiguous
            && pair.Value.Paths.Values.Any(path => path.Display || path.Copies));
        foreach (var group in matching.GroupBy(pair => pair.Key[..pair.Key.IndexOf(':')], StringComparer.Ordinal))
        {
            NbtDocument? document = null;
            if (saved.TryGetValue(group.Key, out var source))
            {
                if (source.Outcome != SourceOutcome.Success) continue;
                var bytes = inventory.Read(source.Location.Artifact);
                if (Hash256.Compute(bytes) != source.ContentHash) throw new ArtifactConflictException("Saved storage changed during analysis");
                document = WorldArtifactEdits.ReadNbt(bytes, limits).Document;
            }
            var found = new List<ListCandidate>();
            foreach (var (id, storage) in group)
            {
                var localId = id[(group.Key.Length + 1)..];
                var rawPaths = storage.OtherPaths.Order(StringComparer.Ordinal).ToArray();
                var listPaths = storage.Paths.Keys.Order(StringComparer.Ordinal).ToArray();
                var equipmentRoots = storage.EquipmentRoots.Order(StringComparer.Ordinal).ToArray();
                foreach (var (path, list) in storage.Paths)
                {
                    if ((!list.Display && !list.Copies) || list.GlyphLiterals is { } glyphs && !glyphs.All(CanPreserveGlyphs) || storage.OtherPaths.Contains(path)
                        || RelatedPath(path, storage.OtherPaths, rawPaths) || RelatedPath(path, storage.Paths.Keys, listPaths)) continue;
                    if (list.EquipmentRoot is { } equipment)
                    {
                        if (opaqueEquipment || !OrderedEquipmentSteps(list) || RelatedPath(equipment, storage.EquipmentRoots, equipmentRoots)) continue;
                    }
                    else if (storage.EquipmentRoots.Contains(path) || RelatedPath(path, storage.EquipmentRoots, equipmentRoots)) continue;
                    var node = SeedPath(localId, path);
                    var seed = document is null ? null : NbtLocator.Resolve(document.Root, node);
                    if (list.GlyphFonts is not null || list.RawGlyphs is not null || list.NumericSource is not null)
                    {
                        if (list is { GlyphFonts: { } fonts, HasRegularWriters: false, HasIntegralWriter: false, HasIncompatibleTransform: false,
                                HasSelfStringTransform: false, HasCrossStorageCopy: false, RequiresScalar: false, NestedRead: false }
                            && (list.RawGlyphs is not null || list.NumericSource is not null || list.Incoming.Count != 0)
                            && fonts.All(font => list.RawGlyphs?.All(glyph => CanPreserveGlyphs(new(font, glyph))) != false)
                            && (list.NumericSource is not { } input
                                || input is { HasIntegralWriter: true, HasRegularWriters: false, HasIncompatibleTransform: false,
                                    RawGlyphs: null, NumericSource: null, Incoming.Count: 0 }
                                && !storage.OtherPaths.Contains(input.Path) && !RelatedPath(input.Path, storage.OtherPaths, rawPaths)
                                && !RelatedPath(input.Path, storage.Paths.Keys, listPaths)
                                && !storage.EquipmentRoots.Contains(input.Path) && !RelatedPath(input.Path, storage.EquipmentRoots, equipmentRoots)
                                && (document is null || NbtLocator.Resolve(document.Root, SeedPath(localId, input.Path)) is
                                    { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 } or { Status: ResolutionStatus.Found, Value: NbtInt })
                                && fonts.All(font => CanPreserveGlyphs(new(font, "-0123456789"))))
                            && seed is null or { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 }
                                or { Status: ResolutionStatus.Found, Value: NbtList { Items.IsEmpty: true } }) yield return new(list);
                        continue;
                    }
                    if (list.HasIntegralWriter || list.UsedByStringTransform) continue;
                    if (list.RequiresScalar)
                    {
                        if (!list.RequiresList && seed is null or { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 }) yield return new(list);
                        continue;
                    }
                    var type = seed?.Value is NbtList existing ? existing.ElementType : NbtType.End;
                    if (!CompatibleWriters(metadata.Profile, list, type)) continue;
                    if (seed is null or { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 }
                        or { Status: ResolutionStatus.Found, Value: NbtList { Items.IsEmpty: true } })
                    {
                        yield return new(list);
                    }
                    else if (seed is { Status: ResolutionStatus.Found, Value: NbtList existingList } && source is not null
                        && !extraction.StorageRules.ContainsKey(source.Location.Artifact) && CanInterpretSeed(source, document!, node, existingList, metadata.Profile))
                    {
                        Charge(localId.Length + path.Length);
                        found.Add(new(list, source.Location.Artifact, new(node, StorageTextKind.InterpretedList)));
                    }
                }
            }
            if (found.Count <= 256)
                foreach (var list in found) yield return list;
        }
    }

    private static bool OrderedEquipmentSteps(ListUses list) => !list.RepeatedEquipmentStep
        && list.EquipmentImport is { Syntax.StartByte: var import } source
        && list.LoreReplacement is { Syntax.StartByte: var replace } rewrite
        && list.EquipmentExport is { Syntax.StartByte: var export } destination
        && source.Artifact == rewrite.Artifact && source.Artifact == destination.Artifact && import < replace && replace < export;

    internal static bool HasUnsafeSeedPath(WorldDiscoveryResult discovery)
    {
        if (discovery.Metadata is not { } metadata) return false;
        bool SeedPath(string path) => WorldLayoutLocator.CommandStorageNamespace(metadata.Profile, path, ignoreCase: true) is not null;
        var namespaced = NamespacedData(metadata.Profile);
        // A canonical lookup can load a differently cased file on Windows even if semantic discovery ignored it.
        return discovery.Inventory.Entries.Any(entry => SeedPath(entry.RelativePath)
                && (entry.IsDirectory || WorldLayoutLocator.CommandStorageNamespace(metadata.Profile, entry.RelativePath) is not { } space
                    || namespaced && space.EndsWith('.')))
            || discovery.Inventory.DirectoryPaths.Any(SeedPath);
    }

    private static bool NamespacedData(CapabilityProfile profile) => profile.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced";

    private static TypedPath SeedPath(string id, string path)
        => new(new[] { "data", "contents", id }.Concat(path.Split('.')).Select(PathSegment.ForKey));

    private bool CanInterpretSeed(DiscoveredSource source, NbtDocument document, TypedPath path, NbtList list, CapabilityProfile runtime)
    {
        try
        {
            var profile = NbtTextExtractor.Profile(source.Location, document, runtime);
            var encoding = profile.Features[ProfileFacet.TextSchema];
            if (runtime.Features[ProfileFacet.TextSchema] != encoding || runtime.Lists != profile.Lists) return false;
            var modern = encoding.Schema == "java-text-nbt";
            for (var index = 0; index < list.Items.Length; index++)
            {
                var item = list.Items[index];
                var node = new TypedPath(path.Segments.Add(PathSegment.ForIndex(index)));
                if (modern && profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && profile.Lists.WrapperKey == ""
                    && item is NbtCompound { Entries.Length: 1 } wrapper && wrapper.Entries[0].Name == "")
                {
                    item = wrapper.Entries[0].Value;
                    node = new(node.Segments.Add(PathSegment.ForKey("")));
                }
                var address = new SourceAddress(source.Location.Artifact, node, "command_storage.component.interpreted", source.Location.Dimension);
                if (!modern && item is not NbtString) return false;
                if (modern ? HasUnprovenNativeFont(item)
                    : TextComponentExtractor.StorageJsonGlyphs(address, ((NbtString)item).Value, profile, limits) is not { } glyphs
                        || !glyphs.Values.All(CanPreserveGlyphs)) return false;
                var references = modern ? NativeComponentReferences.Nbt(address, item, runtime, limits)
                    : NativeComponentReferences.EmbeddedJson(address, ((NbtString)item).Value, profile, limits);
                if (!references.Selectors.IsEmpty || !references.Diagnostics.IsEmpty) return false;
                var text = modern ? TextComponentExtractor.Nbt(address, item, profile, limits)
                    : TextComponentExtractor.EmbeddedJsonString(address, ((NbtString)item).Value, profile, limits);
                if (!text.Diagnostics.IsEmpty || text.Occurrences.Any(occurrence => occurrence.Address.SemanticRole == ClickCommandText.Role)) return false;
                if (modern && profile != runtime)
                {
                    var live = TextComponentExtractor.Nbt(address, item, runtime, limits);
                    if (!live.Occurrences.SequenceEqual(text.Occurrences) || !live.TranslationKeys.SequenceEqual(text.TranslationKeys)) return false;
                }
            }
            return true;
        }
        catch (JsonException) { return false; }
        catch (CodecException error) when (error.Code != CodecError.LimitExceeded) { return false; }
    }
}
