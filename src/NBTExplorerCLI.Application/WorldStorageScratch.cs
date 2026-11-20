namespace NBTExplorerCLI.Application;

internal sealed partial class WorldStorageUseIndex
{
    private readonly HashSet<((string Storage, string Path) Scratch, (string Storage, string Path) Source)> scratchComparisons = [];

    private void ObserveScratchComparison((string Storage, string Path) scratch, (string Storage, string Path) source)
    {
        if (scratchComparisons.Add((scratch, source))) Charge(scratch.Storage.Length + scratch.Path.Length + source.Storage.Length + source.Path.Length);
        GetList(GetStorage(source.Storage), source.Storage, source.Path).RequiresList = true;
    }

    private void BlockUnsafeScratchComparisons()
    {
        var unsafeComparisons = new List<(string Storage, string Path)>();
        foreach (var group in scratchComparisons.GroupBy(pair => pair.Scratch.Storage, System.StringComparer.Ordinal))
        {
            var storage = GetStorage(group.Key);
            var raw = storage.OtherPaths.Order(System.StringComparer.Ordinal).ToArray();
            var equipment = storage.EquipmentRoots.Order(System.StringComparer.Ordinal).ToArray();
            var paths = storage.Paths.Keys.Order(System.StringComparer.Ordinal).ToArray();
            foreach (var destination in group.GroupBy(pair => pair.Scratch.Path, System.StringComparer.Ordinal))
            {
                var path = destination.Key;
                var scratch = storage.Paths.GetValueOrDefault(path);
                if (storage.Ambiguous || scratch is { Display: true } or { Copies: true } or { RequiresList: true }
                    || storage.OtherPaths.Contains(path) || RelatedPath(path, storage.OtherPaths, raw)
                    || storage.EquipmentRoots.Contains(path) || RelatedPath(path, storage.EquipmentRoots, equipment)
                    || RelatedPath(path, storage.Paths.Keys, paths))
                    foreach (var comparison in destination) unsafeComparisons.Add(comparison.Source);
            }
        }
        foreach (var (id, source) in unsafeComparisons) Block(id, source);
        foreach (var comparison in scratchComparisons) Block(comparison.Scratch.Storage, comparison.Scratch.Path);
    }
}
