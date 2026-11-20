using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private Action<SourceAddress>? StoredComponentCallback(TypedPath grammarPath, int start, int end, Dictionary<SnbtSpan, HashSet<TypedPath>>? bookPages = null)
    {
        var original = ComponentCallback(grammarPath, start, true);
        if ((storageAccess is null && bookPages is null) || profile.Features[ProfileFacet.TextSchema].Schema != "java-text-json") return original;
        var bytes = Utf8.GetBytes(source.Command[start..end]);
        var grammar = MinecraftProfiles.SnbtGrammar(profile);
        var visited = new HashSet<long>();
        return address =>
        {
            original?.Invoke(address);
            var bookPage = bookPages is not null && address.SemanticRole == "book.page";
            if (!bookPage && storageAccess is null) return;
            if (address.ComponentPath is null || address.Syntax is not { } syntax || !visited.Add(syntax.StartByte)) return;
            var parsed = Snbt.ParseFragment(bytes, checked((int)syntax.StartByte), grammar, limits);
            if (parsed.Root.Span.LengthBytes != syntax.LengthBytes || parsed.Root.Value is not LogicalMinecraftLeaf { Value: NbtString json }) return;
            var path = new TypedPath(grammarPath.Segments.Concat(syntax.NodePath!.Segments));
            if (bookPage && !json.Value.Contains("nbt", StringComparison.Ordinal) && !json.Value.Contains("\\u", StringComparison.Ordinal)) return;
            var storageNodes = bookPage ? new HashSet<TypedPath>() : null;
            ComponentReferences(NativeJsonStrings.EncodeJavaDocument(json.Value, limits), start, end, path, false, parsed.Root.Span,
                observeStorageOnly: !bookPage, storageNodes: storageNodes);
            if (storageNodes is { Count: > 0 }) bookPages!.Add(parsed.Root.Span, storageNodes);
        };
    }

    private void ObserveDataStorage(List<CommandToken> tokens, int index, TypedPath path, int end, (string Storage, string Path)? storageElement,
        SnbtNode? storageValue, bool resultObserved, IReadOnlyDictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral>? glyphs)
    {
        var attribute = storageAccess is null ? null : AttributeStorage(tokens, index, path, end, storageValue);
        if (storageElement is { } list)
        {
            var value = tokens[^1];
            var use = tokens[index + 5].Text != "set" ? CommandStorageUse.ListElementWrite
                : value.Text.StartsWith('[') ? CommandStorageUse.ListReplacement : CommandStorageUse.ScalarWrite;
            var encoded = MinecraftNbtAdapter.Encode(new("", storageValue!.Value), profile, limits).Root;
            var elementType = use == CommandStorageUse.ListReplacement ? (encoded as NbtList)?.ElementType : encoded.Type;
            ObserveStorage(list.Storage, list.Path, use,
                Address(path, value.Start, value.Text.Length, source.Address.SemanticRole), listElementType: elementType,
                glyphLiterals: glyphs is { Count: > 0 } ? glyphs.Values : null, attribute: attribute);
            if (storageAccess is not null && JsonStorageRuntime() && storageDisplayLists?.Contains(list) != true
                && (value.Text.Contains("storage", StringComparison.Ordinal) || value.Text.Contains("\\u", StringComparison.Ordinal)))
                ComponentReferences(Utf8.GetBytes(value.Text), value.Start, value.Start + value.Text.Length,
                    TextExtractionCollector.At(path, "storage.list.value"), true, observeStorageOnly: true, displayContainer: list);
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
                var use = runtimeDataVersion >= StorageDataVersion && source.Address.SemanticRole == "command.function"
                    && tokens.Count == pathIndex + 1 && tokens[pathIndex].Text.Split('.').All(PlainControlToken)
                    ? CommandStorageUse.ListCardinality : CommandStorageUse.Other;
                ObserveStorageOperand(tokens, target, path, pathIndex, use);
            }
            return;
        }
        if (action is "remove" or "merge")
        {
            if (tokens.Count == pathIndex + 1)
            {
                ObserveUnprovenEquipment(tokens, target, action == "remove" ? pathIndex : tokens.Count);
                if (action != "remove" || runtimeDataVersion < StorageDataVersion || PositionalListPath(tokens[pathIndex].Text) is null)
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
            if (mode == "from" && !resultObserved && storageAccess is not null && (ObserveEquipmentCopy(tokens, index, path, end, pathIndex, sourceEnd, operation)
                || ObserveStorageListCopy(tokens, index, path, end, pathIndex, sourceEnd, operation, attribute))) return;
            ObserveUnprovenEquipment(tokens, target, pathIndex);
            ObserveUnprovenEquipment(tokens, sourceTarget, sourceEnd);
            ObserveStorageOperand(tokens, target, path, pathIndex, attribute: attribute);
            ObserveStorageOperand(tokens, sourceTarget, path, sourceEnd);
            return;
        }
        if (mode == "value" && tokens.Count == sourceMode + 2)
        {
            ObserveUnprovenEquipment(tokens, target, pathIndex);
            ObserveStorageOperand(tokens, target, path, pathIndex, attribute: attribute);
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
            && !(jsonComponents && field.Node.Value is LogicalMinecraftLeaf { Value: NbtString id } && MinecraftIdentifier.ResourceLocation(id.Value)
                || TaggedStorageItems() && StaticNbtMatch.ItemStack(field.Node)))) return false;
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

    private bool ObserveStorageListCopy(List<CommandToken> tokens, int index, TypedPath path, int end, int targetPath, int sourcePath, string operation,
        StorageAttributeOperation? attribute)
    {
        if (source.Address.SemanticRole != "command.function" || runtimeDataVersion < StorageDataVersion
            || profile.Features[ProfileFacet.TextSchema].Schema != "java-text-json" || tokens.Count != sourcePath + 1
            || tokens[index + 2].Text != "storage" || tokens[sourcePath - 2].Text != "storage") return false;
        var fromPath = tokens[sourcePath].Text;
        var appendsScalar = false;
        if (operation != "set")
        {
            if (operation is not ("append" or "prepend" or "insert")) return false;
            if (PositionalListPath(fromPath) is { } list) fromPath = list;
            else appendsScalar = true;
        }
        var toPath = tokens[targetPath].Text;
        if (!fromPath.Split('.').All(PlainControlToken) || !toPath.Split('.').All(PlainControlToken)) return false;
        var from = (MinecraftIdentifier.DefaultNamespace(tokens[sourcePath - 1].Text), fromPath);
        var to = (MinecraftIdentifier.DefaultNamespace(tokens[index + 3].Text), toPath);
        if (from == to) return false;
        var address = Address(path, tokens[index].Start, end - tokens[index].Start, source.Address.SemanticRole);
        Charge(address, from.Item1 + fromPath + to.Item1 + toPath);
        storageAccess!.Invoke(new(to.Item1, toPath, CommandStorageUse.ListCopy, address)
        { CopySource = from, ReplacesList = operation == "set", AppendsScalar = appendsScalar, AttributeOperation = attribute });
        return true;
    }

    private static string? PositionalListPath(string path)
    {
        var bracket = path.IndexOf('[');
        return bracket > 0 && path.EndsWith(']') && (bracket + 2 == path.Length || Integer(path[(bracket + 1)..^1], out _))
            && path[..bracket].Split('.').All(PlainControlToken) ? path[..bracket] : null;
    }

    private (string Storage, string Path)? StorageDisplayWrite(List<CommandToken> tokens, int index)
    {
        if (tokens.Count < index + 8 || tokens[index + 1].Text != "modify" || tokens[index + 2].Text != "storage"
            || !MinecraftIdentifier.ResourceLocation(tokens[index + 3].Text) || !tokens[index + 4].Text.Split('.').All(PlainControlToken))
            return null;
        var operation = tokens[index + 5].Text;
        var mode = index + 6;
        if (operation == "insert")
        {
            if (!Integer(tokens[mode++].Text, out _)) return null;
        }
        else if (operation is not ("append" or "prepend") && (operation != "set"
            || !(tokens[^1].Text.StartsWith('[') || tokens[^1].Text[0] is '\'' or '"' && JsonStorageRuntime()))) return null;
        return tokens.Count == mode + 2 && tokens[mode].Text == "value"
            ? (MinecraftIdentifier.DefaultNamespace(tokens[index + 3].Text), tokens[index + 4].Text) : null;
    }

    private bool ReadStorageDisplayWrite(List<CommandToken> tokens, int index, TypedPath path, (string Storage, string Path)? storageElement,
        IReadOnlyDictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral>? glyphs)
    {
        if (storageDisplayLists is not { Count: > 0 } || storageElement is not { } list || !storageDisplayLists.Contains(list)) return false;
        var value = tokens[^1];
        Component(value.Start, value.Start + value.Text.Length, TextExtractionCollector.At(path, "storage.list.value"),
            storedRole: source.Address.SemanticRole + ".storage.component", storedList: tokens[index + 5].Text == "set" && value.Text.StartsWith('['),
            protectedGlyphs: glyphs);
        return true;
    }

    private bool JsonStorageRuntime() => runtimeDataVersion >= StorageDataVersion
        && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json"
        && (exactRuntimeDataVersion is not int version || MinecraftProfiles.ForDataVersion(version)?.Features[ProfileFacet.TextSchema].Schema == "java-text-json");

    private const string BlankComparisonDiagnostic = "UnresolvedStorageBlankComparison";
    private const string ControlComparisonDiagnostic = "UnresolvedStorageControlComparison";

    private bool ReadIndexedBlankWrite(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (tokens.Count != index + 8 || tokens[index + 1].Text != "modify" || tokens[index + 2].Text != "storage"
            || DataTargetEnd(tokens, index + 2) != index + 4 || tokens[index + 5].Text != "set" || tokens[index + 6].Text != "value"
            || source.Address.SemanticRole != "command.function" || !JsonStorageRuntime()) return false;
        var element = tokens[index + 4].Text;
        if (element.EndsWith("[]", StringComparison.Ordinal) || PositionalListPath(element) is not { } list) return false;
        var token = tokens[^1];
        if (!IsBlankStorageComponent(QuotedOperand(token.Text))) return false;
        var address = Address(path, token.Start, token.Text.Length, source.Address.SemanticRole);
        var use = index == 0 || FixedScoreStore(tokens, index) ? CommandStorageUse.IndexedBlankWrite : CommandStorageUse.Other;
        ObserveStorage(tokens[index + 3].Text, list, use, address, listElementType: NbtType.String);
        Unknown(address, BlankComparisonDiagnostic);
        return true;
    }

    private bool FixedScoreStore(List<CommandToken> tokens, int index)
        => index == 7 && tokens[0].Text == "execute" && tokens[1].Text == "store"
            && tokens[2].Text is "success" or "result" && tokens[3].Text == "score"
            && !tokens[4].Text.StartsWith('@') && ScoreHolder(tokens[4].Text)
            && PlainControlToken(tokens[5].Text) && tokens[6].Text == "run";

    private bool ReadStorageScratchReset(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (index != 0 || tokens.Count != 8 || tokens[1].Text != "modify" || tokens[2].Text != "storage"
            || !MinecraftIdentifier.ResourceLocation(tokens[3].Text)
            || !PlainControlToken(tokens[4].Text) || tokens[4].Text.Contains('.')
            || tokens[5].Text != "set" || tokens[6].Text != "value"
            || source.Address.SemanticRole != "command.function" || !JsonStorageRuntime()
            || QuotedOperand(tokens[7].Text) is not { } operand) return false;
        var blank = IsBlankStorageComponent(operand);
        if (!blank && !(TaggedStorageItems() && StaticNbtMatch.AttributeModifierField("Slot", operand))) return false;
        var storage = MinecraftIdentifier.DefaultNamespace(tokens[3].Text);
        var member = tokens[4].Text;
        if (blank && storageDisplayLists?.Contains((storage, member)) == true) return false;
        var value = tokens[7];
        var address = Address(path, value.Start, value.Text.Length, source.Address.SemanticRole);
        Unknown(address, blank ? BlankComparisonDiagnostic : ControlComparisonDiagnostic);
        if (blank) ObserveStorage(storage, member, CommandStorageUse.ScalarWrite, address, listElementType: NbtType.String);
        lineReset = (storage, member, address, blank);
        return true;
    }

    private bool ReadScratchComparison(List<CommandToken> tokens, int index, TypedPath path)
    {
        if (source.Address.SemanticRole != "command.function" || !JsonStorageRuntime()
            || previousReset is not { } reset || tokens.Count != 17 || !FixedScoreStore(tokens, index)
            || tokens[8].Text != "modify" || tokens[9].Text != "storage" || tokens[12].Text != "set" || tokens[13].Text != "from"
            || tokens[14].Text != "storage" || !MinecraftIdentifier.ResourceLocation(tokens[15].Text)
            || !MinecraftIdentifier.ResourceLocation(tokens[10].Text) || reset.Path != tokens[11].Text) return false;
        var destination = MinecraftIdentifier.DefaultNamespace(tokens[10].Text);
        var scratch = reset.Path;
        var sourceStorage = MinecraftIdentifier.DefaultNamespace(tokens[15].Text);
        var element = tokens[16].Text;
        if (!reset.Blank)
        {
            if (!element.EndsWith(".Slot", StringComparison.Ordinal)) return false;
            element = element[..^".Slot".Length];
        }
        if (destination != reset.Storage || element.EndsWith("[]", StringComparison.Ordinal)
            || PositionalListPath(element) is not { } list
            || sourceStorage == destination && (list == scratch || list.StartsWith(scratch + ".", StringComparison.Ordinal))) return false;
        var target = tokens[11];
        var address = Address(path, target.Start, target.Text.Length, source.Address.SemanticRole);
        Unknown(address, reset.Blank ? BlankComparisonDiagnostic : ControlComparisonDiagnostic);
        if (storageAccess is not null)
        {
            Charge(address, sourceStorage + list + destination + scratch);
            var use = reset.Blank ? CommandStorageUse.ScratchComparison : CommandStorageUse.ScratchControlComparison;
            storageAccess(new(destination, scratch, use, address)
            {
                CopySource = (sourceStorage, list),
                BlankResetAddress = reset.Blank ? reset.Address : null,
                ControlResetAddress = reset.Blank ? null : reset.Address
            });
        }
        return true;
    }

    private SnbtNode? QuotedOperand(string token) => token[0] is '\'' or '"'
        ? Snbt.ParseDocument(Utf8.GetBytes(token), MinecraftProfiles.SnbtGrammar(profile), limits).Root : null;

    private bool IsBlankStorageComponent(SnbtNode? node)
    {
        if (node?.Value is not LogicalMinecraftLeaf { Value: NbtString component }
            || component.Value.AsSpan().TrimStart() is not ['{', ..]) return false;
        try
        {
            using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(component.Value, limits), limits);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1
                && root.TryGetNativeProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                && string.IsNullOrWhiteSpace(NativeJsonStrings.Read(text));
        }
        catch (JsonException) { return false; }
    }

    private bool ResourceStorageLabel(List<CommandToken> tokens, int index, SnbtNode? value)
    {
        if (!JsonStorageRuntime() || StorageDisplayWrite(tokens, index) is null || tokens[index + 5].Text == "set"
            || tokens[^1].Text[0] is not ('\'' or '"')) return false;
        value ??= Snbt.ParseDocument(Utf8.GetBytes(tokens[^1].Text), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
        if (value.Value is not LogicalMinecraftLeaf { Value: NbtString label }
            || label.Value.AsSpan().TrimStart() is not ['{' or '[', ..]) return false;
        try
        {
            using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(label.Value, limits), limits);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object) return ResourceKey(root) is not null;
            return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 2
                && ResourceKey(root[0]) is { } key && key.StartsWith("enchantment.", StringComparison.Ordinal)
                && root[1].ValueKind == JsonValueKind.Object && root[1].EnumerateObject().Count() == 1
                && root[1].TryGetNativeProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                && NativeJsonStrings.Read(text) is { Length: >= 2 and <= 16 } level
                && level[0] == ' ' && RomanLevel.IsMatch(level.AsSpan(1));
        }
        catch (JsonException) { return false; }
    }

    private static readonly Regex RomanLevel = new(@"\AM{0,3}(?:CM|CD|D?C{0,3})(?:XC|XL|L?X{0,3})(?:IX|IV|V?I{0,3})\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static string? ResourceKey(JsonElement component)
    {
        if (component.ValueKind != JsonValueKind.Object || !component.TryGetNativeProperty("translate", out var value)
            || value.ValueKind != JsonValueKind.String) return null;
        var key = NativeJsonStrings.Read(value);
        if (key.Length is < 1 or > 256 || !key.Contains('.') || !key.All(unit => char.IsAsciiLetterOrDigit(unit) || unit is '_' or '-' or '.' or ':'))
            return null;
        foreach (var field in component.EnumerateObject())
        {
            var supported = NativeJsonStrings.PropertyName(field) switch
            {
                "translate" => true,
                "bold" or "italic" or "underlined" or "strikethrough" or "obfuscated" => field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "color" => field.Value.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(field.Value) is { } color
                    && (TeamOption("color", color) || color is ['#', _, _, _, _, _, _] && color.Skip(1).All(Uri.IsHexDigit)),
                _ => false
            };
            if (!supported) return null;
        }
        return key;
    }

    private Dictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral>? StorageGlyphs(SnbtNode value, bool storedList)
    {
        var glyphs = new Dictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral>();
        if (!storedList) return ReadStorageGlyphs(value, TypedPath.Root, glyphs) ? glyphs : null;
        if (value.Value is not LogicalMinecraftList) return null;
        foreach (var child in value.Children)
            if (!ReadStorageGlyphs(child.Node, new([child.Selector]), glyphs)) return null;
        return glyphs;
    }

    private bool ReadStorageGlyphs(SnbtNode node, TypedPath nodePath, Dictionary<(TypedPath Node, TypedPath Component), StorageGlyphLiteral> glyphs)
    {
        if (node.Value is not LogicalMinecraftLeaf { Value: NbtString text }) return false;
        var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, source.Address.SemanticRole + ".storage.component");
        if (TextComponentExtractor.StorageJsonGlyphs(binding, text.Value, profile, limits) is not { } protectedGlyphs) return false;
        foreach (var (path, glyph) in protectedGlyphs) glyphs.Add((nodePath, path), glyph);
        return true;
    }

    private void ObserveStorageOperand(List<CommandToken> tokens, int index, TypedPath path, int? pathIndex = null,
        CommandStorageUse use = CommandStorageUse.Other, StorageAttributeOperation? attribute = null)
    {
        if (storageAccess is null || tokens[index].Text != "storage") return;
        var id = tokens[index + 1];
        var member = pathIndex is { } field && field < tokens.Count ? tokens[field].Text : "";
        ObserveStorage(id.Text, member, use, Address(path, id.Start, id.Text.Length, source.Address.SemanticRole), attribute: attribute);
    }

    private void ObserveStorage(string storage, string path, CommandStorageUse use, SourceAddress address, bool hasUnclassifiedReferences = false,
        NbtType? listElementType = null, IEnumerable<StorageGlyphLiteral>? glyphLiterals = null, (string Storage, string Path)? displayContainer = null,
        StorageAttributeOperation? attribute = null)
    {
        if (!MinecraftIdentifier.ResourceLocation(storage)) return;
        var normalized = MinecraftIdentifier.DefaultNamespace(storage);
        retained = checked(retained + TextExtractionBudget.RecordBytes(address, (long)normalized.Length + path.Length));
        if (retained > limits.MaxDecodedBytes) Limit();
        storageAccess?.Invoke(new(normalized, path, use, address, hasUnclassifiedReferences)
        { ListElementType = listElementType, GlyphLiterals = glyphLiterals, DisplayContainer = displayContainer, AttributeOperation = attribute });
    }
}
