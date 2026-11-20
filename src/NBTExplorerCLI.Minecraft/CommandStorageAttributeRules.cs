using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public enum StorageAttributeShape { List, Modifier, Identifier, Slot, Number, Uuid }

public sealed record StorageAttributeOperand(string Storage, string Path, StorageAttributeShape? Container, StorageAttributeShape? Value)
{
    public bool NativeList => Path == "tag.AttributeModifiers" || Path.EndsWith(".tag.AttributeModifiers", StringComparison.Ordinal);
}

public sealed record StorageAttributeOperation(StorageAttributeOperand Target, StorageAttributeOperand? Source,
    StorageAttributeShape? Literal, bool IsResourceLocation, bool IsEquipmentSlot, SourceAddress Address);

internal sealed partial class CommandTextReader
{
    private StorageAttributeOperation? AttributeStorage(List<CommandToken> tokens, int index, TypedPath path, int end, SnbtNode? parsedValue)
    {
        if (tokens.Count < index + 8 || tokens[index + 1].Text != "modify" || tokens[index + 2].Text != "storage"
            || source.Address.SemanticRole != "command.function" || !TaggedStorageItems()
            || !MinecraftIdentifier.ResourceLocation(tokens[index + 3].Text)) return null;
        var action = tokens[index + 5].Text;
        var append = action is "append" or "prepend" or "insert";
        if (!append && action != "set") return null;
        var mode = index + 6;
        if (action == "insert" && !Integer(tokens[mode++].Text, out _)) return null;
        var target = AttributeOperand(tokens[index + 3].Text, tokens[index + 4].Text);
        if (target is null || append && target.Container is not null) return null;
        if (append) target = target with { Container = StorageAttributeShape.List, Value = StorageAttributeShape.Modifier };

        var address = Address(path, tokens[index].Start, end - tokens[index].Start, source.Address.SemanticRole);
        StorageAttributeOperation operation;
        if (tokens[mode].Text == "from" && tokens.Count == mode + 4 && tokens[mode + 1].Text == "storage"
            && MinecraftIdentifier.ResourceLocation(tokens[mode + 2].Text) && AttributeOperand(tokens[mode + 2].Text, tokens[mode + 3].Text) is { } from)
            operation = new(target, from, null, false, false, address);
        else if (tokens[mode].Text == "value" && tokens.Count == mode + 2)
        {
            var raw = tokens[mode + 1].Text;
            if (raw[0] is not ('\'' or '"' or '{' or '[')) return null;
            var value = parsedValue ?? Snbt.ParseDocument(Utf8.GetBytes(raw), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
            if (value.Value is LogicalMinecraftLeaf { Value: NbtString })
            {
                var identifier = StaticNbtMatch.AttributeModifierField("AttributeName", value);
                var slot = StaticNbtMatch.AttributeModifierField("Slot", value);
                if (!identifier && !slot) return null;
                operation = new(target, null, null, identifier, slot, address);
            }
            else if (StaticNbtMatch.AttributeModifierFields(value))
                operation = new(target, null, StorageAttributeShape.Modifier, false, false, address);
            else if (value.Value is LogicalMinecraftList && !value.Children.IsEmpty && value.Children.All(item => StaticNbtMatch.AttributeModifierFields(item.Node)))
                operation = new(target, null, StorageAttributeShape.List, false, false, address);
            else return null;
        }
        else return null;

        Charge(address, target.Storage + target.Path + operation.Source?.Storage + operation.Source?.Path);
        return operation;
    }

    private StorageAttributeOperand? AttributeOperand(string storage, string path)
    {
        storage = MinecraftIdentifier.DefaultNamespace(storage);
        var bracket = path.IndexOf('[');
        if (bracket < 0)
        {
            if (!path.Split('.').All(PlainControlToken)) return null;
            var dot = path.LastIndexOf('.');
            return dot > 0 && AttributeShape(path[(dot + 1)..]) is { } member
                ? new(storage, path[..dot], StorageAttributeShape.Modifier, member)
                : new(storage, path, null, null);
        }
        if (bracket == 0 || !path[..bracket].Split('.').All(PlainControlToken) || bracket + 1 == path.Length) return null;
        var close = path[bracket + 1] == '{' ? CompoundEnd(path, bracket + 1) : path.IndexOf(']', bracket + 1);
        if (close <= bracket || close >= path.Length || path[close] != ']') return null;
        var selector = path[(bracket + 1)..close];
        if (selector.Length != 0 && !Integer(selector, out _) && (selector[0] != '{'
            || !StaticNbtMatch.AttributeModifierFields(Snbt.ParseDocument(Utf8.GetBytes(selector), MinecraftProfiles.SnbtGrammar(profile), limits).Root))) return null;
        if (close == path.Length - 1) return new(storage, path[..bracket], StorageAttributeShape.List, StorageAttributeShape.Modifier);
        return path[close + 1] == '.' && AttributeShape(path[(close + 2)..]) is { } field
            ? new(storage, path[..bracket], StorageAttributeShape.List, field) : null;
    }

    private static StorageAttributeShape? AttributeShape(string field) => field switch
    {
        "Name" or "AttributeName" => StorageAttributeShape.Identifier,
        "Slot" => StorageAttributeShape.Slot,
        "Amount" or "Operation" => StorageAttributeShape.Number,
        "UUID" => StorageAttributeShape.Uuid,
        _ => null,
    };
}
