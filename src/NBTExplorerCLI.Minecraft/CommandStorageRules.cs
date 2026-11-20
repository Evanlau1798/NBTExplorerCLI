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

    private void ObserveDataStorage(List<CommandToken> tokens, int index, TypedPath path, (string Storage, string Path)? storageElement)
    {
        if (storageElement is { } list)
        {
            var value = tokens[^1];
            ObserveStorage(list.Storage, list.Path, CommandStorageUse.ListElementWrite,
                Address(path, value.Start, value.Text.Length, source.Address.SemanticRole));
            return;
        }
        var target = index + 2;
        var pathIndex = DataTargetEnd(tokens, target);
        if (pathIndex < 0) return;
        var action = tokens[index + 1].Text;
        if (action == "get")
        {
            if (tokens.Count <= pathIndex + 1 || tokens.Count == pathIndex + 2 && Decimal(tokens[pathIndex + 1].Text, out _))
                ObserveStorageOperand(tokens, target, path);
            return;
        }
        if (action is "remove" or "merge")
        {
            if (tokens.Count == pathIndex + 1) ObserveStorageOperand(tokens, target, path);
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
            ObserveStorageOperand(tokens, target, path);
            ObserveStorageOperand(tokens, sourceTarget, path);
            return;
        }
        if (mode == "value" && tokens.Count == sourceMode + 2) ObserveStorageOperand(tokens, target, path);
    }

    private static (string Storage, string Path)? StorageDisplayWrite(List<CommandToken> tokens, int index)
        => tokens.Count == index + 8 && tokens[index + 1].Text == "modify" && tokens[index + 2].Text == "storage"
            && MinecraftIdentifier.ResourceLocation(tokens[index + 3].Text) && tokens[index + 4].Text.Split('.').All(PlainControlToken)
            && tokens[index + 5].Text is "append" or "prepend" && tokens[index + 6].Text == "value"
            ? (MinecraftIdentifier.DefaultNamespace(tokens[index + 3].Text), tokens[index + 4].Text) : null;

    private bool ReadStorageDisplayWrite(List<CommandToken> tokens, TypedPath path, (string Storage, string Path)? storageElement)
    {
        if (storageDisplayLists is not { Count: > 0 } || storageElement is not { } list || !storageDisplayLists.Contains(list)) return false;
        var value = tokens[^1];
        Component(value.Start, value.Start + value.Text.Length, TextExtractionCollector.At(path, "storage.list.value"),
            storedRole: source.Address.SemanticRole + ".storage.component");
        return true;
    }

    private void ObserveStorageOperand(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (storageAccess is null || tokens[index].Text != "storage") return;
        var id = tokens[index + 1];
        ObserveStorage(id.Text, "", CommandStorageUse.Other, Address(path, id.Start, id.Text.Length, source.Address.SemanticRole));
    }

    private void ObserveStorage(string storage, string path, CommandStorageUse use, SourceAddress address, bool hasUnclassifiedReferences = false)
    {
        if (!MinecraftIdentifier.ResourceLocation(storage)) return;
        var normalized = MinecraftIdentifier.DefaultNamespace(storage);
        retained = checked(retained + TextExtractionBudget.RecordBytes(address, (long)normalized.Length + path.Length));
        if (retained > limits.MaxDecodedBytes) Limit();
        storageAccess?.Invoke(new(normalized, path, use, address, hasUnclassifiedReferences));
    }
}
