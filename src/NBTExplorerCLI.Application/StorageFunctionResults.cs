using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed class StorageFunctionResults(Action<int> charge)
{
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> calls = new(StringComparer.Ordinal);
    private readonly Dictionary<ArtifactAddress, HashSet<(string Storage, string Path)>> mutations = [];

    internal void ObserveCall(string? caller, CommandFunctionCall call)
    {
        if (call.ResultObserved && observed.Add(call.Target)) charge(call.Target.Length);
        if (caller is null) return;
        if (!calls.TryGetValue(caller, out var targets))
        {
            charge(caller.Length);
            calls.Add(caller, targets = new(StringComparer.Ordinal));
        }
        if (targets.Add(call.Target)) charge(call.Target.Length);
    }

    internal void ObserveMutation(CommandStorageAccess access)
    {
        if (access.Address.SemanticRole != "command.function" || access.Use is not (CommandStorageUse.ListElementWrite
            or CommandStorageUse.ListReplacement or CommandStorageUse.ListCopy or CommandStorageUse.EquipmentImport or CommandStorageUse.EquipmentExport or CommandStorageUse.ScalarWrite)) return;
        if (!mutations.TryGetValue(access.Address.Artifact, out var paths)) mutations.Add(access.Address.Artifact, paths = []);
        Add((access.Storage, access.Path));
        if (access.CopySource is { } copied) Add(copied);

        void Add((string Storage, string Path) path)
        {
            if (paths.Add(path)) charge(path.Storage.Length + path.Path.Length);
        }
    }

    internal bool BlockObservedMutations(WorldDiscoveryResult discovery, Action<string, string> block, CancellationToken cancellationToken)
    {
        if (observed.Count == 0) return true;
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var id in observed) Visit(id);
        while (pending.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (calls.TryGetValue(id, out var targets))
                foreach (var target in targets) Visit(target);
        }
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in discovery.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Location.Role != WorldSourceRole.Function || WorldLayoutLocator.FunctionId(source.Location) is not { } id || !reachable.Contains(id)) continue;
            // Include every matching definition because the active pack can change at runtime.
            charge(id.Length);
            defined.Add(id);
            if (mutations.TryGetValue(source.Location.Artifact, out var paths))
                foreach (var path in paths) block(path.Storage, path.Path);
        }
        return defined.Count == reachable.Count;

        void Visit(string target)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reachable.Add(target)) return;
            charge(target.Length);
            pending.Enqueue(target);
        }
    }
}
