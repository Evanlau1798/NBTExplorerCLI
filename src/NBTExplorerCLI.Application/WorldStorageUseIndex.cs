using System.Collections.Immutable;
using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed record StorageDisplayPolicy(ImmutableHashSet<(string Storage, string Path)> Lists, ImmutableDictionary<string, Hash256> SourceKeys,
    bool CanBindWrites, ImmutableHashSet<SourceAddress> InterpretedReferences)
{
    internal static StorageDisplayPolicy Empty { get; } = new([], ImmutableDictionary<string, Hash256>.Empty, false, []);
    internal static bool Target(SourceAddress address) => address.Syntax is not null
        && address.SemanticRole.StartsWith("command.function.storage.component", StringComparison.Ordinal);
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
                covered = text is { Diagnostics.IsEmpty: false, Unclassified.IsEmpty: true }
                    && text.Diagnostics.All(diagnostic => index.MatchUse(diagnostic, observed, required, interpretedReferences));
            }
            observed.Clear();
        }
        var lists = index.ReadCompatibleLists(inventory, extraction.Discovery).ToImmutableHashSet();
        if (lists.IsEmpty) return StorageDisplayPolicy.Empty;
        var producers = lists.SelectMany(list => index.uses[list.Storage].Producers.Select(artifact => (Artifact: artifact, List: list)))
            .GroupBy(item => item.Artifact, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('\n', group
                .OrderBy(item => item.List.Storage, StringComparer.Ordinal).ThenBy(item => item.List.Path, StringComparer.Ordinal)
                .Select(item => item.List.Storage + "\0" + item.List.Path)))), StringComparer.Ordinal);
        if (!covered || !required.IsSubsetOf(lists)) return new(lists, producers, false, []);
        var policy = new StorageDisplayPolicy(lists, producers, true, interpretedReferences.ToImmutable());
        if (!policy.ProjectedCoverage(extraction.ReadStorageProducers(policy))) return new(lists, producers, false, []);
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
            case CommandStorageUse.InterpretedDisplay when access.Path.EndsWith("[]", StringComparison.Ordinal):
                MatchPath(ref storage.DisplayPath, access.Path[..^2], storage);
                break;
            case CommandStorageUse.ListElementWrite or CommandStorageUse.ListReplacement when access.Address.SemanticRole == "command.function":
                MatchPath(ref storage.WritePath, access.Path, storage);
                storage.HasElementWrite |= access.Use == CommandStorageUse.ListElementWrite;
                var artifact = access.Address.Artifact.RelativePath;
                if (storage.Producers.Add(artifact)) Charge(artifact.Length);
                break;
            default:
                storage.Ambiguous = true;
                break;
        }
    }

    internal IEnumerable<(string Storage, string Path)> ReadCompatibleLists(IWorldInventory inventory, WorldDiscoveryResult discovery)
    {
        if (!CompleteScope(discovery) || discovery.Metadata is not { } metadata) yield break;
        // Windows folds trailing dots in namespace directories, making even a separately named reader ambiguous.
        if (NamespacedData(metadata.Profile) && uses.Keys.Any(storage => storage[..storage.IndexOf(':')].EndsWith('.'))) yield break;
        var saved = new Dictionary<string, DiscoveredSource>(StringComparer.Ordinal);
        foreach (var source in discovery.Sources)
            if (source.Location is { Role: WorldSourceRole.SavedData, Artifact: { Kind: ArtifactKind.Nbt, ContainerEntry: null } artifact }
                && source.Chunk is null && WorldLayoutLocator.CommandStorageNamespace(metadata.Profile, artifact.RelativePath) is { } space)
                saved.Add(space, source);
        var matching = uses.Where(pair => !pair.Value.Ambiguous && pair.Value.WritePath is { Length: > 0 } && pair.Value.WritePath == pair.Value.DisplayPath);
        foreach (var group in matching.GroupBy(pair => pair.Key[..pair.Key.IndexOf(':')], StringComparer.Ordinal))
        {
            NbtValue? root = null;
            if (saved.TryGetValue(group.Key, out var source))
            {
                if (source.Outcome != SourceOutcome.Success) continue;
                var bytes = inventory.Read(source.Location.Artifact);
                if (Hash256.Compute(bytes) != source.ContentHash) throw new ArtifactConflictException("Saved storage changed during analysis");
                root = WorldArtifactEdits.ReadNbt(bytes, limits).Document.Root;
            }
            foreach (var (id, storage) in group)
            {
                var path = storage.WritePath!;
                var localId = id[(group.Key.Length + 1)..];
                if (root is null || EmptySeed(root, localId, path, storage.HasElementWrite)) yield return (id, path);
            }
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

    private static bool EmptySeed(NbtValue root, string id, string path, bool hasElementWrite)
    {
        var segments = new[] { "data", "contents", id }.Concat(path.Split('.')).Select(PathSegment.ForKey);
        var found = NbtLocator.Resolve(root, new(segments));
        return found is { Status: ResolutionStatus.Missing, SegmentOffset: >= 2 }
            || found is { Status: ResolutionStatus.Found, Value: NbtList { Items.IsEmpty: true } list }
            && (!hasElementWrite || list.ElementType is NbtType.End or NbtType.String);
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
        internal bool HasElementWrite;
        internal bool Ambiguous;
        internal HashSet<string> Producers { get; } = new(StringComparer.Ordinal);
    }

    private sealed record StorageCommandNode(CommandStorageUse Use, ArtifactAddress Artifact, ChunkPosition? Chunk, TypedPath CommandRoot, TypedPath Syntax);
}
