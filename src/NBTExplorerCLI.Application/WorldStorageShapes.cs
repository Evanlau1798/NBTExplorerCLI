using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

internal sealed partial class WorldStorageUseIndex
{
    private void PropagateAssignmentShapes()
    {
        var pending = new Queue<ListUses>(uses.Values.SelectMany(storage => storage.Paths.Values)
            .Where(value => value.Assignments.Count != 0 && (value.RequiresScalar || value.RequiresList)));
        while (pending.TryDequeue(out var value))
        {
            foreach (var peer in value.Assignments)
            {
                var scalar = value.RequiresScalar || peer.RequiresScalar;
                var list = value.RequiresList || peer.RequiresList;
                if (scalar == peer.RequiresScalar && list == peer.RequiresList) continue;
                peer.RequiresScalar = scalar;
                peer.RequiresList = list;
                pending.Enqueue(peer);
            }
        }
    }

    private sealed class StorageUses
    {
        internal Dictionary<string, ListUses> Paths { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> OtherPaths { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> EquipmentRoots { get; } = new(StringComparer.Ordinal);
        internal bool Ambiguous;
    }

    private sealed class ListUses(string storage, string path)
    {
        internal string Storage { get; } = storage;
        internal string Path { get; } = path;
        internal bool Display;
        internal bool RequiresList;
        internal bool RequiresScalar;
        internal string? EquipmentRoot;
        internal SourceAddress? EquipmentImport;
        internal SourceAddress? LoreReplacement;
        internal SourceAddress? EquipmentExport;
        internal bool RepeatedEquipmentStep;
        internal bool Copies => Incoming.Count != 0 || Outgoing.Count != 0 || EquipmentRoot is not null;
        internal HashSet<NbtType> ElementWrites { get; } = [];
        internal HashSet<NbtType> ReplacementTypes { get; } = [];
        internal HashSet<string> Producers { get; } = new(StringComparer.Ordinal);
        internal HashSet<ListUses> Incoming { get; } = [];
        internal HashSet<ListUses> Outgoing { get; } = [];
        internal HashSet<ListUses> Assignments { get; } = [];
    }

    private sealed record ListCandidate(ListUses List, ArtifactAddress? Artifact = null, StorageTextRule? Rule = null);
}
