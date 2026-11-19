using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void ComponentReferences(byte[] bytes, int start, int end, TypedPath grammarPath, bool snbt, SnbtSpan? enclosingString = null, bool itemTree = false)
    {
        if (snbt)
        {
            var grammar = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt" ? SnbtProfile.Java1215 : SnbtProfile.JavaThrough1214;
            var parsed = Snbt.ParseDocument(bytes, grammar, limits);
            if (itemTree)
            {
                if (PotentialReferences(parsed.Root.Value)) Unknown(grammarPath, start, end - start, "UnresolvedItemComponentReferences");
                return;
            }
            VisitNbt(parsed.Root.Value, TypedPath.Root);
            void VisitNbt(LogicalMinecraftValue value, TypedPath path)
            {
                if (value is LogicalMinecraftLeaf { Value: NbtString embeddedJson } && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json")
                {
                    ComponentReferences(NativeJsonStrings.EncodeJavaDocument(embeddedJson.Value, limits), start, end, new(grammarPath.Segments.Concat(path.Segments)), false, parsed.Resolve(path).Span); return;
                }
                if (value is LogicalMinecraftList list)
                { for (var index = 0; index < list.Items.Length; index++) VisitNbt(list.Items[index], At(path, index)); return; }
                if (value is not LogicalMinecraftCompound compound) return;
                var fields = compound.Entries.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
                if (fields.TryGetValue("selector", out var selector) && selector is LogicalMinecraftLeaf { Value: NbtString text })
                    Selector(text.Value, At(path, "selector"), parsed.Resolve(At(path, "selector")).Span);
                if (fields.ContainsKey("nbt") || fields.TryGetValue("score", out var score) && !ComponentReferenceRules.IsSelfScore(score)
                    || fields.TryGetValue("click_event", out var click) && ComponentReferenceRules.Click(click, true, limits) is null)
                    Unknown(new(grammarPath.Segments.Concat(path.Segments)), start, end - start, "DynamicComponentReference");
                if (fields.TryGetValue("extra", out var extra)) VisitNbt(extra, At(path, "extra"));
                if (fields.ContainsKey("translate") && fields.TryGetValue("with", out var arguments)) VisitNbt(arguments, At(path, "with"));
                if ((fields.ContainsKey("selector") || fields.ContainsKey("nbt")) && fields.TryGetValue("separator", out var separator)) VisitNbt(separator, At(path, "separator"));
                if (fields.TryGetValue("hover_event", out var hover) && hover is LogicalMinecraftCompound hoverObject)
                {
                    var action = hoverObject.Entries.SingleOrDefault(entry => entry.Name == "action")?.Value;
                    var content = hoverObject.Entries.SingleOrDefault(entry => entry.Name == "value")?.Value;
                    if (action is LogicalMinecraftLeaf { Value: NbtString { Value: "show_text" } } && content is not null) VisitNbt(content, At(At(path, "hover_event"), "value"));
                }
            }
        }
        else
        {
            using var document = BoundedJson.ReadDecoded(bytes, limits); var spans = NativeJsonStringSpans.Read(bytes, limits); VisitJson(document.RootElement, TypedPath.Root);
            void VisitJson(JsonElement value, TypedPath path)
            {
                if (value.ValueKind == JsonValueKind.Array) { var index = 0; foreach (var child in value.EnumerateArray()) VisitJson(child, At(path, index++)); return; }
                if (value.ValueKind != JsonValueKind.Object) return;
                if (value.TryGetNativeProperty("selector", out var selector) && selector.ValueKind == JsonValueKind.String)
                    Selector(NativeJsonStrings.Read(selector), At(path, "selector"), spans[At(path, "selector")]);
                if (value.TryGetNativeProperty("nbt", out _) || value.TryGetNativeProperty("score", out var score) && !ComponentReferenceRules.IsSelfScore(score)
                    || value.TryGetNativeProperty("clickEvent", out var click) && ComponentReferenceRules.Click(click, false, limits) is null)
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
        void Selector(string selector, TypedPath path, SnbtSpan span)
        {
            var node = new TypedPath(grammarPath.Segments.Concat(path.Segments));
            Selectors([new(selector, 0)], node, (referencePath, _, _, role) => Address(referencePath, start, 0, role,
                (enclosingString ?? span).StartByte, (enclosingString ?? span).LengthBytes, enclosingString is null ? null : path));
        }
    }
    private bool PotentialReferences(LogicalMinecraftValue value)
    {
        if (value is LogicalMinecraftList list) return list.Items.Any(PotentialReferences);
        if (value is LogicalMinecraftCompound compound)
            return compound.Entries.Any(entry => entry.Name switch
            {
                "selector" or "nbt" => true,
                "score" => !ComponentReferenceRules.IsSelfScore(entry.Value),
                "click_event" => ComponentReferenceRules.Click(entry.Value, true, limits) is null,
                "clickEvent" => ComponentReferenceRules.Click(entry.Value, false, limits) is null,
                "minecraft:custom_data" or "custom_data" or "title" or "filtered_title" or "author" or "generation" or "resolved" or "id" or "count" or "Count" or "slot" or "Slot" => false,
                _ => PotentialReferences(entry.Value)
            });
        if (value is LogicalMinecraftLeaf { Value: NbtString text } && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json")
        {
            try
            {
                using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(text.Value, limits), limits);
                return Contains(document.RootElement);
            }
            catch (JsonException) { return false; }
        }
        return false;
        bool Contains(JsonElement element)
            => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Any(Contains)
                : element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any(property => NativeJsonStrings.PropertyName(property) switch
                {
                    "selector" or "nbt" => true,
                    "score" => !ComponentReferenceRules.IsSelfScore(property.Value),
                    "clickEvent" => ComponentReferenceRules.Click(property.Value, false, limits) is null,
                    _ => Contains(property.Value)
                });
    }
}
