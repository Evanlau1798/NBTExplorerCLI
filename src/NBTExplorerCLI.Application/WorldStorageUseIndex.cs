using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed record StorageDisplayPolicy(ImmutableHashSet<(string Storage, string Path)> Lists, ImmutableDictionary<string, Hash256> SourceKeys,
    bool CanBindWrites, ImmutableHashSet<SourceAddress> InterpretedReferences, ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> SeedRules)
{
    internal static StorageDisplayPolicy Empty { get; } = new([], ImmutableDictionary<string, Hash256>.Empty, false, [],
        ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>>.Empty);
    internal static bool Target(SourceAddress address) => address.Syntax is not null
        && address.SemanticRole.StartsWith("command.function.storage.component", StringComparison.Ordinal)
        || address.SemanticRole == "command_storage.component.interpreted"
        || address.SemanticRole.StartsWith("command_storage.component.interpreted.", StringComparison.Ordinal);
    internal static bool TargetsStorage(IEnumerable<PatchOperation> operations) => operations.Any(operation => operation.Targets.Any(Target));
    internal bool Covers(ExtractionDiagnostic diagnostic) => diagnostic.Code == "DynamicComponentReference" && InterpretedReferences.Contains(diagnostic.Address);

    internal bool ProjectedCoverage(IEnumerable<TextSourceExtraction> rows) => rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant
        || row.Outcome == SourceOutcome.Unsupported && row.Text is { Diagnostics.IsEmpty: false, Unclassified.IsEmpty: true } text
        && text.Diagnostics.All(Covers));
}

internal sealed class WorldStorageUseIndex(DecodeLimits limits)
{
    private readonly Dictionary<string, StorageUses> uses = new(StringComparer.Ordinal);
    private long retained;

    internal static StorageDisplayPolicy Capture(WorldTextExtraction extraction, IWorldInventory inventory, CancellationToken cancellationToken)
    {
        if (!CompleteScope(extraction.Discovery) || !extraction.Discovery.Sources.Any(source => source.MayUseCommandStorage)
            || HasUnsafeSeedPath(extraction.Discovery))
            return StorageDisplayPolicy.Empty;
        var index = new WorldStorageUseIndex(extraction.Limits);
        var observed = new Dictionary<StorageCommandNode, CommandStorageAccess>();
        var required = new HashSet<(string Storage, string Path)>();
        var interpretedReferences = ImmutableHashSet.CreateBuilder<SourceAddress>();
        var pendingSeeds = new HashSet<string>(StringComparer.Ordinal);
        var covered = true;
        foreach (var row in extraction.ReadStorageObservations(access =>
        {
            index.Observe(access);
            var use = access.Use == CommandStorageUse.ListReplacement ? CommandStorageUse.ListElementWrite : access.Use;
            if (covered && !access.HasUnclassifiedReferences && access.Address.Syntax?.NodePath is { } syntax
                && (use == CommandStorageUse.ListElementWrite
                    || use == CommandStorageUse.InterpretedDisplay && access.Path.EndsWith("[]", StringComparison.Ordinal)))
                observed.TryAdd(new(use, access.Address.Artifact, access.Address.Chunk, access.Address.NodePath, syntax), access);
        }, () => covered = false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled) return StorageDisplayPolicy.Empty;
            if (covered && row.Outcome == SourceOutcome.Unsupported)
            {
                var text = row.Text;
                var artifact = row.Source.Location.Artifact;
                if (row.DiagnosticCode == "UnclassifiedStorageText" && text is { Diagnostics.IsEmpty: true }
                    && row.Source.Location.Role == WorldSourceRole.SavedData && artifact.Kind == ArtifactKind.Nbt
                    && extraction.LayoutProfile is { } layout && WorldLayoutLocator.CommandStorageNamespace(layout, artifact.RelativePath) is not null)
                {
                    if (pendingSeeds.Add(artifact.RelativePath)) index.Charge(artifact.RelativePath.Length);
                }
                else covered = text is { Diagnostics.IsEmpty: false, Unclassified.IsEmpty: true }
                    && text.Diagnostics.All(diagnostic => index.MatchUse(diagnostic, observed, required, interpretedReferences));
            }
            observed.Clear();
        }
        var seedRules = ImmutableDictionary.CreateBuilder<ArtifactAddress, ImmutableArray<StorageTextRule>>();
        var lists = index.ReadCompatibleLists(inventory, extraction, seedRules).ToImmutableHashSet();
        if (lists.IsEmpty) return StorageDisplayPolicy.Empty;
        var producers = lists.SelectMany(list => index.uses[list.Storage].Producers.Select(artifact => (Artifact: artifact, List: list)))
            .GroupBy(item => item.Artifact, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('\n', group
                .OrderBy(item => item.List.Storage, StringComparer.Ordinal).ThenBy(item => item.List.Path, StringComparer.Ordinal)
                .Select(item => item.List.Storage + "\0" + item.List.Path)))), StringComparer.Ordinal);
        producers = producers.SetItems(seedRules.Select(pair => new KeyValuePair<string, Hash256>(pair.Key.RelativePath,
            Hash256.Compute(Encoding.UTF8.GetBytes(string.Concat(pair.Value.Select(rule => SourceIdentity.Occurrence(
                new SourceAddress(pair.Key, rule.Path, "policy.storage.interpreted")).Digest.Hex).Order(StringComparer.Ordinal)))))));
        var preview = new StorageDisplayPolicy(lists, producers, false, [], seedRules.ToImmutable());
        if (!covered || !required.IsSubsetOf(lists)) return preview;
        var policy = preview with { CanBindWrites = true, InterpretedReferences = interpretedReferences.ToImmutable() };
        if (!policy.ProjectedCoverage(extraction.ReadStorageProjection(policy, pendingSeeds))) return preview;
        var consumerKeys = policy.InterpretedReferences.GroupBy(address => address.Artifact.RelativePath, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, Hash256>(group.Key, Hash256.Compute(Encoding.UTF8.GetBytes(
                (producers.GetValueOrDefault(group.Key)?.Hex ?? "") + string.Concat(group.Select(address => SourceIdentity.Occurrence(address).Digest.Hex).Order(StringComparer.Ordinal))))));
        return policy with { SourceKeys = producers.SetItems(consumerKeys) };
    }

    private bool MatchUse(ExtractionDiagnostic diagnostic, Dictionary<StorageCommandNode, CommandStorageAccess> observed, HashSet<(string Storage, string Path)> required,
        ImmutableHashSet<SourceAddress>.Builder interpretedReferences)
    {
        var use = diagnostic.Code switch
        {
            "UnknownDataTextSchema" => CommandStorageUse.ListElementWrite,
            "DynamicComponentReference" => CommandStorageUse.InterpretedDisplay,
            _ => CommandStorageUse.Other
        };
        var address = diagnostic.Address;
        if (use == CommandStorageUse.Other || address.Syntax?.NodePath is not { } syntax
            || !observed.TryGetValue(new(use, address.Artifact, address.Chunk, address.NodePath, syntax), out var access)) return false;
        var reader = use == CommandStorageUse.InterpretedDisplay;
        var requiredPath = reader ? access.Path[..^2] : access.Path;
        if (required.Add((access.Storage, requiredPath))) Charge(access.Storage.Length + requiredPath.Length);
        if (reader && interpretedReferences.Add(address)) Charge(address.Artifact.RelativePath.Length);
        return true;
    }

    internal static bool HasOpaqueStorage(ReadOnlySpan<byte> json)
        => json.IndexOf("storage"u8) >= 0 || json.IndexOf("\\u"u8) >= 0;

    internal static bool HasOpaqueStorage(NbtValue node) => node switch
    {
        NbtString text => text.Value.Contains("storage", StringComparison.Ordinal) || text.Value.Contains("\\u", StringComparison.Ordinal),
        NbtCompound compound => compound.Entries.Any(entry => entry.Name == "storage" || HasOpaqueStorage(entry.Value)),
        NbtList list => list.Items.Any(HasOpaqueStorage),
        _ => false
    };

    internal void Observe(CommandStorageAccess access)
    {
        if (!uses.TryGetValue(access.Storage, out var storage))
        {
            Charge(access.Storage.Length);
            storage = new();
            uses.Add(access.Storage, storage);
        }
        if (storage.Ambiguous) return;
        switch (access.Use)
        {
            case CommandStorageUse.InterpretedDisplay when access.Path.EndsWith("[]", StringComparison.Ordinal) && PlainPath(access.Path[..^2]):
                MatchPath(ref storage.DisplayPath, access.Path[..^2], storage);
                break;
            case CommandStorageUse.ListElementWrite or CommandStorageUse.ListReplacement when access.Address.SemanticRole == "command.function":
                MatchPath(ref storage.WritePath, access.Path, storage);
                if (access.ListElementType is not { } type) storage.Ambiguous = true;
                else if (access.Use == CommandStorageUse.ListElementWrite) storage.ElementWrites.Add(type);
                else if (type != NbtType.End) storage.ReplacementTypes.Add(type);
                var artifact = access.Address.Artifact.RelativePath;
                if (storage.Producers.Add(artifact)) Charge(artifact.Length);
                break;
            default:
                if (!PlainPath(access.Path)) storage.Ambiguous = true;
                else if (storage.OtherPaths.Add(access.Path)) Charge(access.Path.Length);
                break;
        }
    }

    private IEnumerable<(string Storage, string Path)> ReadCompatibleLists(IWorldInventory inventory, WorldTextExtraction extraction,
        ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>>.Builder seedRules)
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
        var matching = uses.Where(pair => !pair.Value.Ambiguous && pair.Value.DisplayPath is { Length: > 0 } display
            && (pair.Value.WritePath is null || pair.Value.WritePath == display)
            && pair.Value.OtherPaths.All(path => !Overlap(path, display)));
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
            var found = new List<(string Storage, string Path)>();
            var rules = ImmutableArray.CreateBuilder<StorageTextRule>();
            foreach (var (id, storage) in group)
            {
                var path = storage.DisplayPath!;
                var localId = id[(group.Key.Length + 1)..];
                var node = SeedPath(localId, path);
                var seed = document is null ? null : NbtLocator.Resolve(document.Root, node);
                var type = seed?.Value is NbtList existing ? existing.ElementType : NbtType.End;
                if (!CompatibleWriters(metadata.Profile, storage, type)) continue;
                if (seed is null or { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 }
                    or { Status: ResolutionStatus.Found, Value: NbtList { Items.IsEmpty: true } })
                {
                    if (storage.WritePath is not null) yield return (id, path);
                }
                else if (seed is { Status: ResolutionStatus.Found, Value: NbtList list } && source is not null
                    && !extraction.StorageRules.ContainsKey(source.Location.Artifact) && CanInterpretSeed(source, document!, node, list, metadata.Profile))
                {
                    Charge(localId.Length + path.Length);
                    rules.Add(new(node, StorageTextKind.InterpretedList));
                    found.Add((id, path));
                }
            }
            if (rules.Count is 0 or > 256) continue;
            seedRules.Add(source!.Location.Artifact, rules.ToImmutable());
            foreach (var list in found) yield return list;
        }
    }

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

    private static bool CompleteScope(WorldDiscoveryResult discovery)
        => discovery.Complete && discovery.Scope is { Dimensions.IsEmpty: true, Regions.IsEmpty: true, Types.IsEmpty: true, ArtifactPaths.IsEmpty: true };

    private static TypedPath SeedPath(string id, string path)
        => new(new[] { "data", "contents", id }.Concat(path.Split('.')).Select(PathSegment.ForKey));

    private static bool PlainPath(string path) => path.Split('.').All(segment => segment.Length > 0
        && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or ':' or '#'));

    private static bool Overlap(string left, string right) => left == right || Parent(left, right) || Parent(right, left);

    private static bool Parent(string possibleParent, string child) => child.Length > possibleParent.Length
        && child[possibleParent.Length] == '.' && child.StartsWith(possibleParent, StringComparison.Ordinal);

    private static bool CompatibleWriters(CapabilityProfile runtime, StorageUses storage, NbtType seedType)
        => storage.ElementWrites.Count == 0 || MinecraftProfiles.SnbtGrammar(runtime) == SnbtProfile.Java1215
            || storage.ElementWrites.Count == 1 && (seedType == NbtType.End || storage.ElementWrites.Contains(seedType))
            && storage.ReplacementTypes.All(storage.ElementWrites.Contains);

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

    private void MatchPath(ref string? current, string candidate, StorageUses storage)
    {
        if (current is null) { Charge(candidate.Length); current = candidate; }
        else if (current != candidate) storage.Ambiguous = true;
    }

    private void Charge(int characters)
    {
        retained = checked(retained + 256L + 2L * characters);
        if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "World storage use index exceeds retention budget");
    }

    private sealed class StorageUses
    {
        internal string? DisplayPath;
        internal string? WritePath;
        internal HashSet<NbtType> ElementWrites { get; } = [];
        internal HashSet<NbtType> ReplacementTypes { get; } = [];
        internal HashSet<string> OtherPaths { get; } = new(StringComparer.Ordinal);
        internal bool Ambiguous;
        internal HashSet<string> Producers { get; } = new(StringComparer.Ordinal);
    }

    private sealed record StorageCommandNode(CommandStorageUse Use, ArtifactAddress Artifact, ChunkPosition? Chunk, TypedPath CommandRoot, TypedPath Syntax);
}
