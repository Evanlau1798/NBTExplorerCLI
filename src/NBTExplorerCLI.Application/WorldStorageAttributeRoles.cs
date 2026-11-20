using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using Node = (string Storage, string Path);

namespace NBTExplorerCLI.Application;

internal sealed class WorldStorageAttributeRoles(Action<int> charge)
{
    private readonly Dictionary<Node, List<StorageAttributeOperation>> links = [];
    private readonly Dictionary<Node, StorageAttributeShape> native = [];
    private readonly Dictionary<SourceAddress, StorageAttributeOperation> operations = [];

    internal void Observe(StorageAttributeOperation? operation)
    {
        if (operation is null || !operations.TryAdd(operation.Address, operation)) return;
        var target = Key(operation.Target);
        charge(operation.Address.Artifact.RelativePath.Length + target.Storage.Length + target.Path.Length);
        Anchor(operation.Target);
        if (operation.Source is not { } source) return;
        var origin = Key(source);
        Anchor(source);
        AddLink(target);
        if (origin != target) AddLink(origin);

        void AddLink(Node key)
        {
            charge(key.Storage.Length + key.Path.Length);
            if (!links.TryGetValue(key, out var list)) links.Add(key, list = []);
            list.Add(operation);
        }
    }

    internal bool Contains(SourceAddress address) => operations.ContainsKey(address);

    private void Anchor(StorageAttributeOperand operand)
    {
        if (operand.NativeList && native.TryAdd(Key(operand), StorageAttributeShape.List)) charge(operand.Path.Length);
    }

    internal IReadOnlyDictionary<Node, StorageAttributeShape> Prove(CancellationToken cancellationToken)
    {
        var visited = new HashSet<Node>();
        var answer = new Dictionary<Node, StorageAttributeShape>();
        foreach (var (root, shape) in native)
        {
            if (!visited.Add(root)) continue;
            var consistent = true;
            var pending = new Queue<Node>();
            var component = new Dictionary<Node, StorageAttributeShape> { [root] = shape };
            pending.Enqueue(root);
            while (pending.TryDequeue(out var node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!links.TryGetValue(node, out var edges)) continue;
                foreach (var edge in edges)
                {
                    var source = edge.Source!;
                    var (here, there) = Key(edge.Target) == node ? (edge.Target, source) : (source, edge.Target);
                    var expected = Project(here, component[node]);
                    var next = Key(there);
                    if (expected is null || there.Value is { } value && value != expected)
                    {
                        consistent = false;
                        continue;
                    }
                    var thereShape = there.Container ?? expected.Value;
                    if (native.TryGetValue(next, out var anchor) && anchor != thereShape) consistent = false;
                    if (component.TryGetValue(next, out var known)) { consistent &= thereShape == known; continue; }
                    visited.Add(next);
                    charge(next.Storage.Length + next.Path.Length);
                    component.Add(next, thereShape);
                    pending.Enqueue(next);
                }
            }
            if (consistent)
                foreach (var (member, type) in component) answer.TryAdd(member, type);
        }
        return answer;
    }

    internal bool Covers(SourceAddress address, IReadOnlyDictionary<Node, StorageAttributeShape> roles)
    {
        var operation = operations[address];
        if (!roles.TryGetValue(Key(operation.Target), out var target)) return false;
        var role = Project(operation.Target, target);
        if (role is null) return false;
        if (operation.Source is { } operand)
            return roles.TryGetValue(Key(operand), out var source) && Project(operand, source) == role;
        return role == operation.Literal
            || role == StorageAttributeShape.Identifier && operation.IsResourceLocation
            || role == StorageAttributeShape.Slot && operation.IsEquipmentSlot;
    }

    private static StorageAttributeShape? Project(StorageAttributeOperand operand, StorageAttributeShape input)
        => operand.Container is null ? input : operand.Container == input ? operand.Value : null;

    private static Node Key(StorageAttributeOperand operand) => (operand.Storage, operand.Path);
}
