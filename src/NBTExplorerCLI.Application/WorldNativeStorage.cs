using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

public sealed partial class WorldTextExtraction
{
    private TextExtractionResult ReadNbtText(DiscoveredSource source, NbtDocument document, IReadOnlyList<StorageTextRule> rules,
        Action<CommandStorageAccess>? storageAccess, Action? opaqueStorage, bool inspectSignDependencies)
    {
        if (opaqueStorage is null || !WorldStorageUseIndex.HasOpaqueStorage(document.Root))
            return NbtTextExtractor.Extract(source.Location, document, source.Chunk, Limits, rules, request.Scope, LayoutProfile);

        var pages = new Dictionary<TypedPath, List<SourceAddress>>();
        var result = NbtTextExtractor.Extract(source.Location, document, source.Chunk, Limits, rules, request.Scope, LayoutProfile, root =>
        {
            if (root.ComponentPath is null || root.SemanticRole is not ("book.page" or "lectern.book.page")) return;
            if (!pages.TryGetValue(root.NodePath, out var roots)) pages.Add(root.NodePath, roots = []);
            roots.Add(root);
        });
        var covered = new HashSet<TypedPath>();
        if (inspectSignDependencies)
            foreach (var command in result.PendingCommands)
                covered.Add(command.Address.NodePath);
        if (storageAccess is not null && pages.Count != 0 && LayoutProfile?.Features[ProfileFacet.TextSchema].Schema == "java-text-json"
            && NbtTextExtractor.Profile(source.Location, document, LayoutProfile).Features[ProfileFacet.TextSchema].Schema == "java-text-json")
        {
            foreach (var (path, roots) in pages)
                if (NbtLocator.Resolve(document.Root, path).Value is NbtString encoded && WorldStorageUseIndex.HasOpaqueStorage(encoded)
                    && NativeBookStorage.Observe(encoded.Value, roots, Limits, storageAccess)) covered.Add(path);
        }
        if (UnclassifiedStorage(document.Root, TypedPath.Root, covered)) opaqueStorage();
        return result;
    }

    private static bool UnclassifiedStorage(NbtValue node, TypedPath path, HashSet<TypedPath> covered)
    {
        if (covered.Count == 0) return WorldStorageUseIndex.HasOpaqueStorage(node);
        if (node is NbtString) return !covered.Contains(path) && WorldStorageUseIndex.HasOpaqueStorage(node);
        if (node is NbtCompound compound)
            return compound.Entries.Any(field => field.Name == "storage" || UnclassifiedStorage(field.Value, new(path.Segments.Add(PathSegment.ForKey(field.Name))), covered));
        return node is NbtList list && list.Items.Select((item, index) => (item, index))
            .Any(child => UnclassifiedStorage(child.item, new(path.Segments.Add(PathSegment.ForIndex(child.index))), covered));
    }
}
