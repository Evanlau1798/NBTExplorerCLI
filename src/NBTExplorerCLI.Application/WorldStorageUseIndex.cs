using System.Collections.Immutable;
using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed record StorageDisplayPolicy(ImmutableHashSet<(string Storage, string Path)> Lists, ImmutableDictionary<string, Hash256> SourceKeys,
    bool CanBindWrites, ImmutableHashSet<ExtractionDiagnostic> ResolvedDiagnostics, ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> SeedRules)
{
    internal ImmutableDictionary<ArtifactAddress, ImmutableHashSet<string>> RetainedSeeds { get; init; }
        = ImmutableDictionary<ArtifactAddress, ImmutableHashSet<string>>.Empty;
    internal static StorageDisplayPolicy Empty { get; } = new([], ImmutableDictionary<string, Hash256>.Empty, false, [],
        ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>>.Empty);
    internal static bool Target(SourceAddress address) => TextSlot.IsInterpretedStorage(address);
    internal static bool TargetsStorage(IEnumerable<PatchOperation> operations) => operations.Any(operation => operation.Targets.Any(Target));
    internal bool Covers(ExtractionDiagnostic diagnostic) => ResolvedDiagnostics.Contains(diagnostic);

    internal bool ProjectedCoverage(IEnumerable<TextSourceExtraction> rows, Func<ExtractionDiagnostic, bool>? resolve = null)
        => rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant
        || row.Outcome == SourceOutcome.Unsupported && row.Text is { Diagnostics.IsEmpty: false, Unclassified.IsEmpty: true } text
        && text.Diagnostics.All(diagnostic => Covers(diagnostic) || resolve?.Invoke(diagnostic) == true));
}

internal sealed partial class WorldStorageUseIndex(DecodeLimits limits)
{
    private const string TaggedItemLorePath = ".tag.display.Lore";
    private readonly Dictionary<string, StorageUses> uses = new(StringComparer.Ordinal);
    private bool opaqueEquipment;
    private long retained;

    internal static StorageDisplayPolicy Capture(WorldTextExtraction extraction, IWorldInventory inventory, CancellationToken cancellationToken)
    {
        if (!CompleteScope(extraction.Discovery) || !extraction.Discovery.Sources.Any(source => source.MayUseCommandStorage
                || SavedStorageNamespace(extraction, source) is not null)
            || HasUnsafeSeedPath(extraction.Discovery))
            return StorageDisplayPolicy.Empty;
        var index = new WorldStorageUseIndex(extraction.Limits);
        var functionResults = new StorageFunctionResults(index.Charge);
        var observed = new Dictionary<StorageCommandNode, CommandStorageAccess>();
        var nested = new Dictionary<StorageCommandNode, CommandStorageAccess>();
        var exact = new Dictionary<SourceAddress, CommandStorageAccess>();
        var required = new HashSet<(string Storage, string Path)>();
        var resolvedDiagnostics = ImmutableHashSet.CreateBuilder<ExtractionDiagnostic>();
        var pendingSeeds = new HashSet<string>(StringComparer.Ordinal);
        var covered = true;
        foreach (var row in extraction.ReadStorageObservations(access =>
        {
            index.Observe(access);
            functionResults.ObserveMutation(access);
            if (covered && access.Use is CommandStorageUse.ListCopy or CommandStorageUse.EquipmentExport
                or CommandStorageUse.IndexedBlankWrite or CommandStorageUse.ScratchComparison or CommandStorageUse.ScratchControlComparison)
            {
                exact.TryAdd(access.Address, access);
                if ((access.BlankResetAddress ?? access.ControlResetAddress) is { } reset) exact.TryAdd(reset, access);
            }
            var use = access.Use is CommandStorageUse.ListReplacement or CommandStorageUse.ScalarWrite ? CommandStorageUse.ListElementWrite : access.Use;
            if (covered && !access.HasUnclassifiedReferences && access.Address.Syntax?.NodePath is { } syntax
                && (use == CommandStorageUse.ListElementWrite
                    || use == CommandStorageUse.InterpretedDisplay && PlainPath(DisplayPath(access.Path))))
            {
                var node = new StorageCommandNode(use, access.Address.Artifact, access.Address.Chunk, access.Address.NodePath, syntax);
                if (access.DisplayContainer is null) observed.TryAdd(node, access);
                else if (nested.TryAdd(node, access)) index.Charge(access.Address.Artifact.RelativePath.Length + access.Path.Length);
            }
        }, () => covered = false, () => index.opaqueEquipment = true, functionResults.ObserveCall, index.ObserveFont))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index.opaqueDefaultFont || row.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled) return StorageDisplayPolicy.Empty;
            if (covered && row.Outcome == SourceOutcome.Unsupported)
            {
                var text = row.Text;
                var artifact = row.Source.Location.Artifact;
                if (row.DiagnosticCode == "UnclassifiedStorageText" && text is { Diagnostics.IsEmpty: true }
                    && SavedStorageNamespace(extraction, row.Source) is { } space)
                {
                    if (pendingSeeds.Add(artifact.RelativePath)) index.Charge(artifact.RelativePath.Length);
                    if (!extraction.StorageRules.ContainsKey(artifact)) index.RecordUnclassifiedSeed(artifact, space, text.Unclassified);
                }
                else covered = text is { Diagnostics.IsEmpty: false, Unclassified.IsEmpty: true }
                    && text.Diagnostics.All(diagnostic => index.MatchUse(diagnostic, observed, exact, required, resolvedDiagnostics));
            }
            observed.Clear();
            exact.Clear();
        }
        if (!functionResults.BlockObservedMutations(extraction.Discovery, index.Block, cancellationToken)) covered = false;
        index.BlockUnsafeScratchComparisons();
        index.PropagateAssignmentShapes();
        var compatible = index.ReadCompatibleLists(inventory, extraction).ToDictionary(candidate => candidate.List);
        var authorized = ClosedDisplayLists(compatible).ToHashSet();
        var equipment = index.uses.Values.SelectMany(storage => storage.Paths.Values).Where(list => list.EquipmentRoot is not null).ToArray();
        if (authorized.Any(list => list.EquipmentRoot is not null) && (equipment.Count(list => list.EquipmentExport is not null) > 1
            || equipment.Any(list => list.EquipmentImport is not null && !authorized.Contains(list))))
        {
            // Dynamic selectors may alias other equipment transfers in this world.
            foreach (var list in compatible.Keys.Where(list => list.EquipmentRoot is not null).ToArray()) compatible.Remove(list);
            authorized = ClosedDisplayLists(compatible).ToHashSet();
        }
        var lists = authorized.Select(list => (list.Storage, list.Path)).ToImmutableHashSet();
        var seedRules = authorized.Select(list => compatible[list]).Where(candidate => candidate.Rule is not null)
            .GroupBy(candidate => candidate.Artifact!)
            .ToImmutableDictionary(group => group.Key, group => group.Select(candidate => candidate.Rule!).ToImmutableArray());
        var producers = authorized.SelectMany(list => list.Producers.Select(artifact => (Artifact: artifact, List: list)))
            .GroupBy(item => item.Artifact, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('\n', group
                .OrderBy(item => item.List.Storage, StringComparer.Ordinal).ThenBy(item => item.List.Path, StringComparer.Ordinal)
                .Select(item => item.List.Storage + "\0" + item.List.Path)))), StringComparer.Ordinal);
        var preview = new StorageDisplayPolicy(lists, WithSeedKeys(producers, seedRules), false, [], seedRules);
        if (!covered || !required.IsSubsetOf(lists)) return preview;
        var retainedSeeds = index.RetainUnreferencedSeeds(seedRules);
        if (lists.IsEmpty && resolvedDiagnostics.Count == 0 && retainedSeeds.IsEmpty) return StorageDisplayPolicy.Empty;
        producers = WithSeedKeys(producers, seedRules, retainedSeeds);
        StorageDisplayPolicy BoundPolicy()
        {
            var resolved = resolvedDiagnostics.ToImmutable();
            var consumerKeys = resolved.Select(diagnostic => diagnostic.Address).GroupBy(address => address.Artifact.RelativePath, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, Hash256>(group.Key, Hash256.Compute(Encoding.UTF8.GetBytes(
                (producers.GetValueOrDefault(group.Key)?.Hex ?? "") + string.Concat(group.Select(address => SourceIdentity.Occurrence(address).Digest.Hex).Order(StringComparer.Ordinal))))));
            return preview with { CanBindWrites = true, ResolvedDiagnostics = resolved, RetainedSeeds = retainedSeeds, SourceKeys = producers.SetItems(consumerKeys) };
        }
        var policy = BoundPolicy();
        if (!policy.ProjectedCoverage(extraction.ReadStorageProjection(policy, pendingSeeds), ResolveNested)) return preview;
        return resolvedDiagnostics.Count == policy.ResolvedDiagnostics.Count ? policy : BoundPolicy();

        bool ResolveNested(ExtractionDiagnostic diagnostic)
        {
            var address = diagnostic.Address;
            if (diagnostic.Code != "DynamicComponentReference" || address.Syntax?.NodePath is not { } syntax
                || !nested.TryGetValue(new(CommandStorageUse.InterpretedDisplay, address.Artifact, address.Chunk, address.NodePath, syntax), out var read)
                || !(read.DisplayContainer is { } container && lists.Contains(container) && lists.Contains((read.Storage, DisplayPath(read.Path))))) return false;
            if (resolvedDiagnostics.Add(diagnostic)) index.Charge(address.Artifact.RelativePath.Length);
            return true;
        }
    }

    private bool MatchUse(ExtractionDiagnostic diagnostic, Dictionary<StorageCommandNode, CommandStorageAccess> observed,
        Dictionary<SourceAddress, CommandStorageAccess> exact, HashSet<(string Storage, string Path)> required,
        ImmutableHashSet<ExtractionDiagnostic>.Builder resolvedDiagnostics)
    {
        if (diagnostic.Code is "UnresolvedStorageBlankComparison" or "UnresolvedStorageControlComparison"
            && exact.TryGetValue(diagnostic.Address, out var comparison))
        {
            if (comparison.Use != CommandStorageUse.ScratchControlComparison)
                Require(comparison.CopySource ?? (comparison.Storage, comparison.Path));
            Resolve();
            return true;
        }
        if (diagnostic.Code == "UnknownDataTextSchema" && exact.TryGetValue(diagnostic.Address, out var copy))
        {
            if (copy.CopySource is { } from) Require(from);
            Require((copy.Storage, copy.Use == CommandStorageUse.EquipmentExport ? copy.Path + TaggedItemLorePath : copy.Path));
            Resolve();
            return true;
        }
        var use = diagnostic.Code switch
        {
            "UnknownDataTextSchema" or "UnresolvedStorageBlankComparison" => CommandStorageUse.ListElementWrite,
            "DynamicComponentReference" => CommandStorageUse.InterpretedDisplay,
            _ => CommandStorageUse.Other
        };
        var address = diagnostic.Address;
        if (use == CommandStorageUse.Other || address.Syntax?.NodePath is not { } syntax
            || !observed.TryGetValue(new(use, address.Artifact, address.Chunk, address.NodePath, syntax), out var access)) return false;
        var reader = use == CommandStorageUse.InterpretedDisplay;
        var requiredPath = reader ? DisplayPath(access.Path) : access.Path;
        Require((access.Storage, requiredPath));
        if (reader) Resolve();
        return true;

        void Require((string Storage, string Path) target)
        { if (required.Add(target)) Charge(target.Storage.Length + target.Path.Length); }
        void Resolve()
        { if (resolvedDiagnostics.Add(diagnostic)) Charge(diagnostic.Address.Artifact.RelativePath.Length); }
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

    internal static bool HasOpaqueEquipment(ReadOnlySpan<byte> json)
        => json.IndexOf("copy_nbt"u8) >= 0 || json.IndexOf("\"nbt\""u8) >= 0 || json.IndexOf("\\u"u8) >= 0;

    internal static bool HasOpaqueEquipment(NbtValue node) => node switch
    {
        NbtString text => text.Value.Contains("\"nbt\"", StringComparison.Ordinal) || text.Value.Contains("copy_nbt", StringComparison.Ordinal)
            || text.Value.Contains("\\u", StringComparison.Ordinal),
        NbtCompound compound => compound.Entries.Any(entry => entry.Name == "nbt" || HasOpaqueEquipment(entry.Value)),
        NbtList list => list.Items.Any(HasOpaqueEquipment),
        _ => false
    };

    internal void Observe(CommandStorageAccess access)
    {
        var storage = GetStorage(access.Storage);
        switch (access.Use)
        {
            case CommandStorageUse.ScratchComparison or CommandStorageUse.ScratchControlComparison when access.CopySource is { } input:
                ObserveScratchComparison((access.Storage, access.Path), input, access.Use == CommandStorageUse.ScratchControlComparison);
                break;
            case CommandStorageUse.Presence:
                break;
            case CommandStorageUse.ListCardinality when PlainPath(access.Path):
                GetList(storage, access.Storage, access.Path).RequiresList = true;
                break;
            case CommandStorageUse.InterpretedDisplay when !access.HasUnclassifiedReferences && PlainPath(DisplayPath(access.Path)):
                var subject = GetList(storage, access.Storage, DisplayPath(access.Path));
                if (access.DisplayContainer is { } container)
                {
                    var owner = GetList(GetStorage(container.Storage), container.Storage, container.Path);
                    owner.NestedRead = true;
                    Link(subject, owner);
                }
                else subject.Display = true;
                if (access.Path.EndsWith("[]", StringComparison.Ordinal)) subject.RequiresList = true;
                else subject.RequiresScalar = true;
                break;
            case CommandStorageUse.ListElementWrite or CommandStorageUse.ListReplacement or CommandStorageUse.ScalarWrite or CommandStorageUse.IndexedBlankWrite
                when access.Address.SemanticRole == "command.function":
                var list = GetList(storage, access.Storage, access.Path);
                if (access.ListElementType is not { } type) storage.Ambiguous = true;
                else if (access.Use == CommandStorageUse.ScalarWrite)
                {
                    list.RequiresScalar = true;
                    if (type != NbtType.String) Block(access.Storage, access.Path);
                }
                else
                {
                    list.RequiresList = true;
                    if (access.Use is CommandStorageUse.ListElementWrite or CommandStorageUse.IndexedBlankWrite) list.ElementWrites.Add(type);
                    else if (type != NbtType.End) list.ReplacementTypes.Add(type);
                }
                var artifact = access.Address.Artifact.RelativePath;
                if (list.Producers.Add(artifact)) Charge(artifact.Length);
                if (access.GlyphLiterals is { } glyphs)
                    foreach (var glyph in glyphs)
                        if ((list.GlyphLiterals ??= []).Add(glyph)) Charge(glyph.Font.Length + glyph.Text.Length);
                break;
            case CommandStorageUse.ListCopy when access.CopySource is { } source:
                var from = GetList(GetStorage(source.Storage), source.Storage, source.Path);
                var to = GetList(storage, access.Storage, access.Path);
                Link(from, to);
                if (access.ReplacesList)
                {
                    if (from.Assignments.Add(to)) { Charge(0); to.Assignments.Add(from); }
                }
                else
                {
                    to.RequiresList = true;
                    if (access.AppendsScalar) from.RequiresScalar = true;
                    else from.RequiresList = true;
                }
                if (access.ReplacesList && access.Path.EndsWith(TaggedItemLorePath, StringComparison.Ordinal))
                    RecordEquipmentStep(ref to.LoreReplacement, access.Address, to);
                break;
            case CommandStorageUse.EquipmentImport or CommandStorageUse.EquipmentExport:
                if (storage.EquipmentRoots.Add(access.Path)) Charge(access.Path.Length);
                var lore = GetList(storage, access.Storage, access.Path + TaggedItemLorePath);
                lore.RequiresList = true;
                lore.EquipmentRoot = access.Path;
                if (access.Use == CommandStorageUse.EquipmentImport)
                    RecordEquipmentStep(ref lore.EquipmentImport, access.Address, lore);
                else
                {
                    RecordEquipmentStep(ref lore.EquipmentExport, access.Address, lore);
                    lore.Display = true;
                }
                break;
            default:
                Block(access.Storage, access.Path);
                break;
        }
    }

    private void Block(string id, string path)
    {
        var storage = GetStorage(id);
        var prefix = PlainAncestor(path);
        if (prefix.Length == 0) storage.Ambiguous = true;
        else if (storage.OtherPaths.Add(prefix)) Charge(prefix.Length);
    }

    private void Link(ListUses from, ListUses to)
    {
        if (!from.Outgoing.Add(to)) return;
        Charge(0);
        to.Incoming.Add(from);
    }

    private void RecordEquipmentStep(ref SourceAddress? previous, SourceAddress address, ListUses list)
    {
        if (previous is not null) list.RepeatedEquipmentStep = true;
        else
        {
            Charge(address.Artifact.RelativePath.Length);
            previous = address;
        }
    }

    private StorageUses GetStorage(string id)
    {
        if (uses.TryGetValue(id, out var storage)) return storage;
        Charge(id.Length);
        return uses[id] = new();
    }

    private ListUses GetList(StorageUses storage, string id, string path)
    {
        if (storage.Paths.TryGetValue(path, out var list)) return list;
        Charge(path.Length);
        return storage.Paths[path] = new(id, path);
    }

    private static bool CompleteScope(WorldDiscoveryResult discovery)
        => discovery.Complete && discovery.Scope is { Dimensions.IsEmpty: true, Regions.IsEmpty: true, Types.IsEmpty: true, ArtifactPaths.IsEmpty: true };

    private static string DisplayPath(string path) => path.EndsWith("[]", StringComparison.Ordinal) ? path[..^2] : path;

    private static bool PlainPath(string path) => path.Length != 0 && PlainAncestor(path) == path;

    private static string PlainAncestor(string path)
    {
        var segment = 0;
        for (var index = 0; index < path.Length; index++)
        {
            var character = path[index];
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or ':' or '#') continue;
            if (index > segment && character == '.') { segment = index + 1; continue; }
            if (index > segment && character is '[' or '{') return path[..index];
            return segment == 0 ? "" : path[..(segment - 1)];
        }
        return segment == path.Length && segment > 0 ? path[..(segment - 1)] : path;
    }

    private static bool RelatedPath(string path, ICollection<string> paths, string[] ordered)
    {
        for (var dot = path.IndexOf('.'); dot >= 0; dot = path.IndexOf('.', dot + 1))
            if (paths.Contains(path[..dot])) return true;
        var prefix = path + ".";
        var index = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
        index = index < 0 ? ~index : index;
        return index < ordered.Length && ordered[index].StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool CompatibleWriters(CapabilityProfile runtime, ListUses list, NbtType seedType)
    {
        if (list.Copies)
            return runtime.Features[ProfileFacet.TextSchema].Schema == "java-text-json" && seedType is NbtType.End or NbtType.String
                && list.ElementWrites.All(type => type == NbtType.String) && list.ReplacementTypes.All(type => type == NbtType.String);
        return list.ElementWrites.Count == 0 || MinecraftProfiles.SnbtGrammar(runtime) == SnbtProfile.Java1215
            || list.ElementWrites.Count == 1 && (seedType == NbtType.End || list.ElementWrites.Contains(seedType))
            && list.ReplacementTypes.All(list.ElementWrites.Contains);
    }

    private static IEnumerable<ListUses> ClosedDisplayLists(Dictionary<ListUses, ListCandidate> compatible)
    {
        var visible = new HashSet<ListUses>();
        var pending = new Queue<ListUses>(compatible.Keys.Where(list => list.Display));
        while (pending.TryDequeue(out var list))
        {
            if (!compatible.ContainsKey(list) || !visible.Add(list)) continue;
            foreach (var source in list.Incoming) pending.Enqueue(source);
        }

        var visited = new HashSet<ListUses>();
        foreach (var start in visible)
        {
            if (!visited.Add(start)) continue;
            var connected = new List<ListUses> { start };
            for (var index = 0; index < connected.Count; index++)
            {
                foreach (var neighbor in connected[index].Incoming)
                    if (visited.Add(neighbor)) connected.Add(neighbor);
                foreach (var neighbor in connected[index].Outgoing)
                    if (visited.Add(neighbor)) connected.Add(neighbor);
            }
            if (!connected.All(visible.Contains) || connected.Any(list => list.NestedRead) && HasCycle(connected)
                || !connected.Any(list => list.Producers.Count != 0 || compatible[list].Rule is not null)) continue;
            foreach (var list in connected) yield return list;
        }
    }

    private static bool HasCycle(IReadOnlyCollection<ListUses> connected)
    {
        var indegrees = connected.ToDictionary(list => list, list => list.Incoming.Count);
        var pending = new Queue<ListUses>(connected.Where(list => indegrees[list] == 0));
        var visited = 0;
        while (pending.TryDequeue(out var value))
        {
            visited++;
            foreach (var destination in value.Outgoing)
                if (--indegrees[destination] == 0) pending.Enqueue(destination);
        }
        return visited != connected.Count;
    }

    private void Charge(int characters)
    {
        retained = checked(retained + 256L + 2L * characters);
        if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "World storage use index exceeds retention budget");
    }

    private sealed record StorageCommandNode(CommandStorageUse Use, ArtifactAddress Artifact, ChunkPosition? Chunk, TypedPath CommandRoot, TypedPath Syntax);
}
