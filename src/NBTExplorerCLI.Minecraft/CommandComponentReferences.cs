using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void ComponentReferences(byte[] bytes, int start, int end, TypedPath grammarPath, bool snbt, SnbtSpan? enclosingString = null, bool itemTree = false,
        bool commandTag = false, bool observeStorageOnly = false, IReadOnlyDictionary<SnbtSpan, HashSet<TypedPath>>? bookPages = null,
        ISet<TypedPath>? storageNodes = null, (string Storage, string Path)? displayContainer = null)
    {
        if (snbt)
        {
            var grammar = MinecraftProfiles.SnbtGrammar(profile);
            var parsed = commandTag ? Snbt.ParseCommandTag(bytes, grammar, limits) : Snbt.ParseDocument(bytes, grammar, limits);
            if (itemTree)
            {
                if (PotentialReferences(parsed.Root, bookPages)) Unknown(grammarPath, start, end - start, "UnresolvedItemComponentReferences");
                return;
            }
            VisitNbt(parsed.Root.Value, TypedPath.Root);
            void VisitNbt(LogicalMinecraftValue value, TypedPath path)
            {
                if (value is LogicalMinecraftLeaf { Value: NbtString embeddedJson } && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json")
                {
                    ComponentReferences(NativeJsonStrings.EncodeJavaDocument(embeddedJson.Value, limits), start, end,
                        new(grammarPath.Segments.Concat(path.Segments)), false, parsed.Resolve(path).Span,
                        observeStorageOnly: observeStorageOnly, displayContainer: displayContainer); return;
                }
                if (value is LogicalMinecraftList list)
                { for (var index = 0; index < list.Items.Length; index++) VisitNbt(list.Items[index], At(path, index)); return; }
                if (value is not LogicalMinecraftCompound compound) return;
                var fields = compound.Entries.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
                if (fields.TryGetValue("selector", out var selector) && selector is LogicalMinecraftLeaf { Value: NbtString text })
                    Selector(text.Value, At(path, "selector"), parsed.Resolve(At(path, "selector")).Span);
                if (storageAccess is not null && fields.GetValueOrDefault("nbt") is LogicalMinecraftLeaf { Value: NbtString nbtPath }
                    && fields.GetValueOrDefault("storage") is LogicalMinecraftLeaf { Value: NbtString storage })
                {
                    var interpreted = fields.GetValueOrDefault("interpret") is LogicalMinecraftLeaf { Value: NbtByte { Value: 1 } }
                        && fields.GetValueOrDefault("plain") is null or LogicalMinecraftLeaf { Value: NbtByte { Value: 0 } }
                        && fields.GetValueOrDefault("type") is null or LogicalMinecraftLeaf { Value: NbtString { Value: "nbt" } }
                        && !ComponentReferenceRules.OtherComponentContent(fields.ContainsKey);
                    var unknownClick = fields.TryGetValue("click_event", out var clickData) && ComponentReferenceRules.Click(clickData, grammar, limits) is null;
                    ReportStorage(storage.Value, nbtPath.Value, interpreted, path, unknownClick);
                }
                if (fields.ContainsKey("nbt") || fields.TryGetValue("score", out var score) && !ComponentReferenceRules.HasFixedScoreHolder(score)
                    || fields.TryGetValue("click_event", out var click) && ComponentReferenceRules.Click(click, grammar, limits) is null)
                    Unknown(new(grammarPath.Segments.Concat(path.Segments)), start, end - start, "DynamicComponentReference");
                if (fields.TryGetValue("extra", out var extra)) VisitNbt(extra, At(path, "extra"));
                if (fields.ContainsKey("translate") && fields.TryGetValue("with", out var arguments)) VisitNbt(arguments, At(path, "with"));
                if ((fields.ContainsKey("selector") || fields.ContainsKey("nbt")) && fields.TryGetValue("separator", out var separator)) VisitNbt(separator, At(path, "separator"));
                if (fields.TryGetValue("hover_event", out var hover) && hover is LogicalMinecraftCompound hoverObject)
                {
                    var action = hoverObject.Entries.SingleOrDefault(entry => entry.Name == "action")?.Value;
                    var member = MinecraftProfiles.NativeHoverField(profile);
                    var content = hoverObject.Entries.SingleOrDefault(entry => entry.Name == member)?.Value;
                    if (action is LogicalMinecraftLeaf { Value: NbtString { Value: "show_text" } } && content is not null) VisitNbt(content, At(At(path, "hover_event"), member));
                }
            }
        }
        else
        {
            using var document = BoundedJson.ReadDecoded(bytes, limits);
            var spans = observeStorageOnly || storageNodes is not null ? null : NativeJsonStringSpans.Read(bytes, limits);
            VisitJson(document.RootElement, TypedPath.Root);
            void VisitJson(JsonElement value, TypedPath path)
            {
                if (value.ValueKind == JsonValueKind.Array) { var index = 0; foreach (var child in value.EnumerateArray()) VisitJson(child, At(path, index++)); return; }
                if (value.ValueKind != JsonValueKind.Object) return;
                if (spans is not null && value.TryGetNativeProperty("selector", out var selector) && selector.ValueKind == JsonValueKind.String)
                    Selector(NativeJsonStrings.Read(selector), At(path, "selector"), spans[At(path, "selector")]);
                if ((storageAccess is not null || storageNodes is not null) && ComponentReferenceRules.JsonStorage(value, limits) is { } storage)
                {
                    storageNodes?.Add(path);
                    ReportStorage(storage.Id, storage.Path, storage.Interpreted, path,
                        storage.OpaqueClick || ComponentReferenceRules.UsesExplicitJsonFont(document.RootElement, path));
                }
                if (!observeStorageOnly && (value.TryGetNativeProperty("nbt", out _) || value.TryGetNativeProperty("score", out var score) && !ComponentReferenceRules.HasFixedScoreHolder(score)
                    || value.TryGetNativeProperty("clickEvent", out var click) && ComponentReferenceRules.Click(click, null, limits) is null))
                    Unknown(new(grammarPath.Segments.Concat(path.Segments)), start, end - start, "DynamicComponentReference");
                if (value.TryGetNativeProperty("extra", out var extra)) VisitJson(extra, At(path, "extra"));
                if (value.TryGetNativeProperty("translate", out _) && value.TryGetNativeProperty("with", out var arguments)) VisitJson(arguments, At(path, "with"));
                if ((value.TryGetNativeProperty("selector", out _) || value.TryGetNativeProperty("nbt", out _)) && value.TryGetNativeProperty("separator", out var separator)) VisitJson(separator, At(path, "separator"));
                if (value.TryGetNativeProperty("hoverEvent", out var hover) && hover.TryGetNativeProperty("action", out var action) && action.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(action) == "show_text")
                {
                    var field = hover.TryGetNativeProperty("contents", out var content) ? "contents" : "value";
                    if (field == "contents" || hover.TryGetNativeProperty(field, out content)) VisitJson(content, At(At(path, "hoverEvent"), field));
                }
            }
        }
        void ReportStorage(string storage, string path, bool interpreted, TypedPath component, bool hasUnclassifiedReferences)
        {
            var address = Address(new(grammarPath.Segments.Concat(component.Segments)), start, end - start, source.Address.SemanticRole,
                enclosingString?.StartByte, enclosingString?.LengthBytes);
            ObserveStorage(storage, path, interpreted ? CommandStorageUse.InterpretedDisplay : CommandStorageUse.LiteralDisplay,
                address, hasUnclassifiedReferences, displayContainer: displayContainer);
        }
        void Selector(string selector, TypedPath path, SnbtSpan span)
        {
            var node = new TypedPath(grammarPath.Segments.Concat(path.Segments));
            Selectors([new(selector, 0)], node, (referencePath, _, _, role) => Address(referencePath, start, 0, role,
                (enclosingString ?? span).StartByte, (enclosingString ?? span).LengthBytes, enclosingString is null ? null : path));
        }
    }
    private bool PotentialReferences(SnbtNode node, IReadOnlyDictionary<SnbtSpan, HashSet<TypedPath>>? bookPages)
    {
        if (node.Value is LogicalMinecraftList) return node.Children.Any(child => PotentialReferences(child.Node, bookPages));
        if (node.Value is LogicalMinecraftCompound)
            return node.Children.Any(child => child.Selector.Key switch
            {
                "selector" or "nbt" => true,
                "score" => !ComponentReferenceRules.HasFixedScoreHolder(child.Node.Value),
                "click_event" => ComponentReferenceRules.Click(child.Node.Value, MinecraftProfiles.SnbtGrammar(profile), limits) is null,
                "clickEvent" => ComponentReferenceRules.Click(child.Node.Value, null, limits) is null,
                "minecraft:custom_data" or "custom_data" or "title" or "filtered_title" or "author" or "generation" or "resolved" or "id" or "count" or "Count" or "slot" or "Slot" => false,
                _ => PotentialReferences(child.Node, bookPages)
            });
        if (node.Value is LogicalMinecraftLeaf { Value: NbtString text } && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json")
        {
            try
            {
                using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(text.Value, limits), limits);
                var handled = bookPages?.GetValueOrDefault(node.Span);
                return Contains(document.RootElement, handled is null ? null : TypedPath.Root, handled);
            }
            catch (JsonException) { return false; }
        }
        return false;
        bool Contains(JsonElement element, TypedPath? path, HashSet<TypedPath>? handled)
            => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select((child, index) => (child, index))
                .Any(item => Contains(item.child, path is null ? null : At(path, item.index), handled))
                : element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any(property => NativeJsonStrings.PropertyName(property) switch
                {
                    "selector" => true,
                    "nbt" => path is null || handled?.Contains(path) != true,
                    "score" => !ComponentReferenceRules.HasFixedScoreHolder(property.Value),
                    "clickEvent" => ComponentReferenceRules.Click(property.Value, null, limits) is null,
                    var key => Contains(property.Value, path is null ? null : At(path, key), handled)
                });
    }
}
