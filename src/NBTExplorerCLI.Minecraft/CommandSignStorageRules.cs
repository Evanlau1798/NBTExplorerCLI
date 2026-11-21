using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private bool ReadObservedSignResult(List<CommandToken> tokens, int index, TypedPath path, int end, bool resultObserved, bool valueStored)
    {
        if (tokens.Count <= index + 6 || tokens[index + 2].Text != "block"
            || DataTargetEnd(tokens, index + 2) != index + 6) return false;
        var member = tokens[index + 6].Text;
        if (!SignTextSource(member, out var isCollection) && !SignComponentDescendant(member)) return false;
        var effect = tokens[index + 1].Text switch
        {
            "get" when !isCollection => CommandSignEffect.ReadsStringValue,
            "modify" => CommandSignEffect.MutatesText,
            _ => (CommandSignEffect?)null,
        };
        if (effect is not { } effectKind) return false;
        var address = Address(path, tokens[index].Start, end - tokens[index].Start, source.Address.SemanticRole);
        if (signEffect is not null && source.Address.SemanticRole == "command.function") signEffect(address, effectKind);
        if (!resultObserved || effectKind == CommandSignEffect.ReadsStringValue && !valueStored) return false;
        Unknown(address, "UnresolvedObservedSignResult");
        return true;
    }

    private static bool SignComponentDescendant(ReadOnlySpan<char> path)
    {
        if (!SignMessagePrefix(path, out _, out _, out var suffix) || suffix.IsEmpty || suffix[0] != '[') return false;
        var end = suffix.IndexOf(']');
        return end >= 1 && end + 1 < suffix.Length && suffix[end + 1] is '.' or '[';
    }

    private bool ReadRepeatedSignStyles(int start, int end, TypedPath path)
    {
        if (source.Address.SemanticRole != "command.function" || exactRuntimeDataVersion is null || !JsonStorageRuntime()) return false;
        var operand = source.Command[start..end];
        if (QuotedOperand(operand)?.Value is not LogicalMinecraftLeaf { Value: NbtString component }) return false;
        var bytes = NativeJsonStrings.EncodeJavaDocument(component.Value, limits);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = Math.Max(1, limits.MaxDepth) });
        var array = json.RootElement.ValueKind == JsonValueKind.Array;
        if (array && json.RootElement.GetArrayLength() > limits.MaxCollectionElements) Limit();
        var bindings = new List<(string Storage, string Path)>();
        var repeated = false;
        long entries = 0;
        if (array)
        {
            foreach (var value in json.RootElement.EnumerateArray()) if (!Read(value)) return false;
        }
        else if (!Read(json.RootElement)) return false;
        if (!repeated) return false;
        // Bind only the storage reads so the authored sign component stays byte-identical.
        for (var i = 0; i < bindings.Count; i++)
        {
            var address = Address(array ? At(path, i) : path, start, end - start, source.Address.SemanticRole);
            var (storage, member) = bindings[i];
            ObserveStorage(storage, member, CommandStorageUse.InterpretedDisplay, address);
            Unknown(address, "DynamicComponentReference");
        }
        return true;

        bool Read(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object) return false;
            var fields = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (++entries > limits.MaxContainerEntries || 1L + bindings.Count + entries > limits.MaxNodes) Limit();
                var name = NativeJsonStrings.PropertyName(property);
                var item = property.Value;
                var kind = item.ValueKind;
                var style = name is "bold" or "italic" or "underlined" or "strikethrough" or "obfuscated";
                if (!fields.TryAdd(name, kind))
                {
                    if (!style || fields[name] != kind) return false;
                    repeated = true;
                }
                if (name is "nbt" or "storage" && kind == JsonValueKind.String || name == "interpret" && kind == JsonValueKind.True
                    || style && kind is JsonValueKind.True or JsonValueKind.False) continue;
                if (name != "color" || kind != JsonValueKind.String) return false;
                var color = NativeJsonStrings.Read(item);
                if (!(color != "reset" && TeamOption("color", color)
                    || color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit))) return false;
            }
            if (ComponentReferenceRules.JsonStorage(value, limits) is not { Interpreted: true } read) return false;
            bindings.Add((read.Id, read.Path));
            return true;
        }
    }
}
