using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed class StorageFunctionResults(Action<int> charge)
{
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private readonly HashSet<string> observedValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> calls = new(StringComparer.Ordinal);
    private readonly Dictionary<ArtifactAddress, HashSet<(string Storage, string Path)>> mutations = [];
    private readonly HashSet<ArtifactAddress> signReads = [];
    private readonly HashSet<ArtifactAddress> signMutations = [];

    internal void ObserveCall(string? caller, CommandFunctionCall call)
    {
        if (call.ResultObserved && observed.Add(call.Target)) charge(call.Target.Length);
        if (call.StoresValue && observedValues.Add(call.Target)) charge(call.Target.Length);
        if (caller is null) return;
        if (!calls.TryGetValue(caller, out var targets))
        {
            charge(caller.Length);
            calls.Add(caller, targets = new(StringComparer.Ordinal));
        }
        if (targets.Add(call.Target)) charge(call.Target.Length);
    }

    internal void ObserveSign(SourceAddress address, CommandSignEffect effect)
    {
        var artifacts = effect == CommandSignEffect.ReadsStringValue ? signReads : signMutations;
        if (artifacts.Add(address.Artifact)) charge(address.Artifact.RelativePath.Length);
    }

    internal bool HasObservedSignEffects(WorldDiscoveryResult discovery, CancellationToken cancellationToken)
    {
        // 23w41a changed function stores to observe explicit return results.
        if (discovery.Metadata?.DataVersion is not < 3681) return false;
        return Reaches(observedValues, signReads) || Reaches(observed, signMutations);

        bool Reaches(HashSet<string> roots, HashSet<ArtifactAddress> effects)
        {
            if (roots.Count == 0 || effects.Count == 0) return false;
            var reachable = Reachable(roots, cancellationToken);
            return reachable.Any(id => id.StartsWith('#')) || discovery.Sources.Any(source => effects.Contains(source.Location.Artifact)
                && WorldLayoutLocator.FunctionId(source.Location) is { } id && reachable.Contains(id));
        }
    }

    internal void ObserveMutation(CommandStorageAccess access)
    {
        if (access.Address.SemanticRole != "command.function" || access.Use is not (CommandStorageUse.ListElementWrite
            or CommandStorageUse.ListReplacement or CommandStorageUse.ListCopy or CommandStorageUse.EquipmentImport or CommandStorageUse.EquipmentExport
            or CommandStorageUse.ScalarWrite or CommandStorageUse.ScratchComparison or CommandStorageUse.IndexedBlankWrite or CommandStorageUse.SignImport)) return;
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
        if (HasObservedSignEffects(discovery, cancellationToken)) return false;
        var reachable = Reachable(observed, cancellationToken);
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
    }

    private HashSet<string> Reachable(HashSet<string> roots, CancellationToken cancellationToken)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var id in roots) Visit(id);
        while (pending.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (calls.TryGetValue(id, out var targets))
                foreach (var target in targets) Visit(target);
        }
        return reachable;

        void Visit(string target)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reachable.Add(target)) return;
            charge(target.Length);
            pending.Enqueue(target);
        }
    }
}
