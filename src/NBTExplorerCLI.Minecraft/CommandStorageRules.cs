using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private Action<SourceAddress>? StoredComponentCallback(TypedPath grammarPath, int start, int end)
    {
        var original = ComponentCallback(grammarPath, start, true);
        if (storageAccess is null || profile.Features[ProfileFacet.TextSchema].Schema != "java-text-json") return original;
        var bytes = Utf8.GetBytes(source.Command[start..end]);
        var grammar = MinecraftProfiles.SnbtGrammar(profile);
        var visited = new HashSet<long>();
        return address =>
        {
            original?.Invoke(address);
            if (address.ComponentPath is null || address.Syntax is not { } syntax || !visited.Add(syntax.StartByte)) return;
            var parsed = Snbt.ParseFragment(bytes, checked((int)syntax.StartByte), grammar, limits);
            if (parsed.Root.Span.LengthBytes != syntax.LengthBytes || parsed.Root.Value is not LogicalMinecraftLeaf { Value: NbtString json }) return;
            var path = new TypedPath(grammarPath.Segments.Concat(syntax.NodePath!.Segments));
            ComponentReferences(NativeJsonStrings.EncodeJavaDocument(json.Value, limits), start, end, path, false, parsed.Root.Span, observeStorageOnly: true);
        };
    }

    private void ObserveDataStorage(List<CommandToken> tokens, int index, TypedPath path, int end, (string Storage, string Path)? storageElement, SnbtNode? storageValue)
    {
        if (storageElement is { } list)
        {
            var value = tokens[^1];
            var use = tokens[index + 5].Text == "set" ? CommandStorageUse.ListReplacement : CommandStorageUse.ListElementWrite;
            var encoded = MinecraftNbtAdapter.Encode(new("", storageValue!.Value), profile, limits).Root;
            var elementType = use == CommandStorageUse.ListReplacement ? (encoded as NbtList)?.ElementType : encoded.Type;
            ObserveStorage(list.Storage, list.Path, use,
                Address(path, value.Start, value.Text.Length, source.Address.SemanticRole), listElementType: elementType);
            return;
        }
        var target = index + 2;
        var pathIndex = DataTargetEnd(tokens, target);
        if (pathIndex < 0) return;
        var action = tokens[index + 1].Text;
        if (action == "get")
        {
            if (tokens.Count <= pathIndex + 1 || tokens.Count == pathIndex + 2 && Decimal(tokens[pathIndex + 1].Text, out _))
            {
                ObserveUnprovenEquipment(tokens, target, pathIndex);
                ObserveStorageOperand(tokens, target, path, pathIndex);
            }
            return;
        }
        if (action is "remove" or "merge")
        {
            if (tokens.Count == pathIndex + 1)
            {
                ObserveUnprovenEquipment(tokens, target, action == "remove" ? pathIndex : tokens.Count);
                ObserveStorageOperand(tokens, target, path, action == "remove" ? pathIndex : null);
            }
            return;
        }
        if (action != "modify" || pathIndex + 3 >= tokens.Count) return;
        var operation = tokens[pathIndex + 1].Text;
        var sourceMode = pathIndex + 2;
        if (operation == "insert")
        {
            if (!Integer(tokens[sourceMode++].Text, out _)) return;
        }
        else if (operation is not ("set" or "append" or "prepend" or "merge")) return;
        var mode = tokens[sourceMode].Text;
        if (mode is "from" or "string")
        {
            var sourceTarget = sourceMode + 1;
            var sourceEnd = DataTargetEnd(tokens, sourceTarget);
            if (sourceEnd < 0 || tokens.Count > sourceEnd + (mode == "from" ? 1 : 3)) return;
            for (var offset = sourceEnd + 1; offset < tokens.Count; offset++)
                if (!Integer(tokens[offset].Text, out _)) return;
            if (mode == "from" && storageAccess is not null && (ObserveEquipmentCopy(tokens, index, path, end, pathIndex, sourceEnd, operation)
                || ObserveStorageListCopy(tokens, index, path, end, pathIndex, sourceEnd, operation))) return;
            ObserveUnprovenEquipment(tokens, target, pathIndex);
            ObserveUnprovenEquipment(tokens, sourceTarget, sourceEnd);
            ObserveStorageOperand(tokens, target, path, pathIndex);
            ObserveStorageOperand(tokens, sourceTarget, path, sourceEnd);
            return;
        }
        if (mode == "value" && tokens.Count == sourceMode + 2)
        {
            ObserveUnprovenEquipment(tokens, target, pathIndex);
            ObserveStorageOperand(tokens, target, path, pathIndex);
        }
    }

    private void ObserveUnprovenEquipment(List<CommandToken> tokens, int target, int path)
    {
        if (opaqueEquipment is null) return;
        var field = path < tokens.Count ? tokens[path].Text : "";
        if (tokens[target].Text == "entity" && field != "CustomName" && !NumericEntitySource(field)
            || tokens[target].Text == "block" && !SignTextSource(field)) opaqueEquipment();
    }

    private bool StorageRootCondition(List<CommandToken> tokens, int target, int path, TypedPath commandPath)
    {
        var matcher = tokens[path].Text;
        if (tokens[target].Text != "storage" || runtimeDataVersion < StorageDataVersion || !matcher.StartsWith('{')
            || CompoundEnd(matcher, 0) != matcher.Length) return false;
        var root = Snbt.ParseDocument(Utf8.GetBytes(matcher), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
        var jsonComponents = exactRuntimeDataVersion is int version
            && MinecraftProfiles.ForDataVersion(version)?.Features[ProfileFacet.TextSchema].Schema == "java-text-json";
        if (root.Value is not LogicalMinecraftCompound || root.Children.Any(field => field.Selector.Key is not { } key
            || !PlainControlToken(key) || key.Contains('.') || field.Node.ContainsStringValues
            && !(jsonComponents && field.Node.Value is LogicalMinecraftLeaf { Value: NbtString id } && MinecraftIdentifier.ResourceLocation(id.Value)))) return false;
        if (storageAccess is not null)
        {
            var storage = tokens[target + 1];
            var address = Address(commandPath, storage.Start, storage.Text.Length, source.Address.SemanticRole);
            foreach (var field in root.Children) ObserveStorage(storage.Text, field.Selector.Key!, CommandStorageUse.Other, address);
        }
        return true;
    }

    private bool ObserveEquipmentCopy(List<CommandToken> tokens, int index, TypedPath path, int end, int targetPath, int sourcePath, string operation)
    {
        if (index != 0 || operation != "set" || source.Address.SemanticRole != "command.function" || tokens.Count != sourcePath + 1
            || exactRuntimeDataVersion is not int version || version < StorageDataVersion
            || profile.Features[ProfileFacet.ItemSchema].Schema != "java-item-tags-1.20.4"
            || MinecraftProfiles.ForDataVersion(version)?.Features[ProfileFacet.ItemSchema] != profile.Features[ProfileFacet.ItemSchema]) return false;
        var importing = tokens[2].Text == "storage" && tokens[sourcePath - 2].Text == "entity";
        var exporting = tokens[2].Text == "entity" && tokens[sourcePath - 2].Text == "storage";
        if (!importing && !exporting) return false;
        var equipment = tokens[importing ? sourcePath : targetPath].Text;
        var bracket = equipment.IndexOf('[');
        if (bracket < 0 || equipment.Length != bracket + 3 || equipment[^1] != ']'
            || !(equipment[..bracket] == "HandItems" && equipment[bracket + 1] is '0' or '1'
                || equipment[..bracket] == "ArmorItems" && equipment[bracket + 1] is >= '0' and <= '3')) return false;
        var storage = importing ? targetPath : sourcePath;
        if (!tokens[storage].Text.Split('.').All(PlainControlToken)) return false;
        ObserveStorage(tokens[storage - 1].Text, tokens[storage].Text, importing ? CommandStorageUse.EquipmentImport : CommandStorageUse.EquipmentExport,
            Address(path, tokens[0].Start, end - tokens[0].Start, source.Address.SemanticRole));
        return true;
    }

    private bool ObserveStorageListCopy(List<CommandToken> tokens, int index, TypedPath path, int end, int targetPath, int sourcePath, string operation)
    {
        // Keep execute wrappers unresolved until their use of the mutation's result is known.
        if (index != 0 || source.Address.SemanticRole != "command.function" || runtimeDataVersion < StorageDataVersion
            || profile.Features[ProfileFacet.TextSchema].Schema != "java-text-json" || tokens.Count != sourcePath + 1
            || tokens[2].Text != "storage" || tokens[sourcePath - 2].Text != "storage") return false;
        var fromPath = tokens[sourcePath].Text;
        if (operation != "set")
        {
            if (operation is not ("append" or "prepend") || !fromPath.EndsWith("[]", StringComparison.Ordinal)) return false;
            fromPath = fromPath[..^2];
        }
        var toPath = tokens[targetPath].Text;
        if (!fromPath.Split('.').All(PlainControlToken) || !toPath.Split('.').All(PlainControlToken)) return false;
        var from = (MinecraftIdentifier.DefaultNamespace(tokens[sourcePath - 1].Text), fromPath);
        var to = (MinecraftIdentifier.DefaultNamespace(tokens[3].Text), toPath);
        if (from == to) return false;
        var address = Address(path, tokens[0].Start, end - tokens[0].Start, source.Address.SemanticRole);
        Charge(address, from.Item1 + fromPath + to.Item1 + toPath);
        storageAccess!.Invoke(new(to.Item1, toPath, CommandStorageUse.ListCopy, address) { CopySource = from, ReplacesList = operation == "set" });
        return true;
    }

    private static (string Storage, string Path)? StorageDisplayWrite(List<CommandToken> tokens, int index)
        => tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "storage"
            && MinecraftIdentifier.ResourceLocation(tokens[index + 3].Text) && tokens[index + 4].Text.Split('.').All(PlainControlToken)
            && (tokens[index + 5].Text is "append" or "prepend" || tokens[index + 5].Text == "set" && tokens[^1].Text.StartsWith('['))
            && tokens[index + 6].Text == "value"
            ? (MinecraftIdentifier.DefaultNamespace(tokens[index + 3].Text), tokens[index + 4].Text) : null;

    private bool ReadStorageDisplayWrite(List<CommandToken> tokens, int index, TypedPath path, (string Storage, string Path)? storageElement)
    {
        if (storageDisplayLists is not { Count: > 0 } || storageElement is not { } list || !storageDisplayLists.Contains(list)) return false;
        var value = tokens[^1];
        Component(value.Start, value.Start + value.Text.Length, TextExtractionCollector.At(path, "storage.list.value"),
            storedRole: source.Address.SemanticRole + ".storage.component", storedList: tokens[index + 5].Text == "set");
        return true;
    }

    private void ObserveStorageOperand(List<CommandToken> tokens, int index, TypedPath path, int? pathIndex = null,
        CommandStorageUse use = CommandStorageUse.Other)
    {
        if (storageAccess is null || tokens[index].Text != "storage") return;
        var id = tokens[index + 1];
        var member = pathIndex is { } field && field < tokens.Count ? tokens[field].Text : "";
        ObserveStorage(id.Text, member, use, Address(path, id.Start, id.Text.Length, source.Address.SemanticRole));
    }

    private void ObserveStorage(string storage, string path, CommandStorageUse use, SourceAddress address, bool hasUnclassifiedReferences = false, NbtType? listElementType = null)
    {
        if (!MinecraftIdentifier.ResourceLocation(storage)) return;
        var normalized = MinecraftIdentifier.DefaultNamespace(storage);
        retained = checked(retained + TextExtractionBudget.RecordBytes(address, (long)normalized.Length + path.Length));
        if (retained > limits.MaxDecodedBytes) Limit();
        storageAccess?.Invoke(new(normalized, path, use, address, hasUnclassifiedReferences) { ListElementType = listElementType });
    }
}
