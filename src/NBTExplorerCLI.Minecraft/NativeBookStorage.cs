using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

public static class NativeBookStorage
{
    public static bool Observe(string page, IReadOnlyList<SourceAddress> componentRoots, DecodeLimits limits, Action<CommandStorageAccess> report)
    {
        using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(page, limits), limits);
        var storageNodes = new HashSet<TypedPath>();
        var literals = new HashSet<TypedPath>();
        foreach (var root in componentRoots)
        {
            var path = root.ComponentPath!;
            var value = NativeComponentReferences.JsonAt(document.RootElement, path);
            if (value.ValueKind == JsonValueKind.String) literals.Add(path);
            if (value.ValueKind != JsonValueKind.Object) continue;
            if (value.TryGetNativeProperty("text", out var text) && text.ValueKind == JsonValueKind.String) literals.Add(At(path, "text"));
            if (ComponentReferenceRules.JsonStorage(value, limits) is not { } storage) continue;
            storageNodes.Add(path);
            report(new(MinecraftIdentifier.DefaultNamespace(storage.Id), storage.Path,
                storage.Interpreted ? CommandStorageUse.InterpretedDisplay : CommandStorageUse.LiteralDisplay, root,
                storage.OpaqueClick || ComponentReferenceRules.UsesExplicitJsonFont(document.RootElement, path)));
        }
        return !Unclassified(document.RootElement, TypedPath.Root);

        bool Unclassified(JsonElement value, TypedPath path)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in value.EnumerateObject())
                {
                    var key = NativeJsonStrings.PropertyName(field);
                    if (key is "nbt" or "storage")
                    {
                        if (!storageNodes.Contains(path)) return true;
                    }
                    else if (key == "clickEvent" || Unclassified(field.Value, At(path, key))) return true;
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray()) if (Unclassified(item, At(path, index++))) return true;
            }
            else if (value.ValueKind == JsonValueKind.String && !literals.Contains(path))
            {
                var text = NativeJsonStrings.Read(value);
                return text.Contains("storage", StringComparison.Ordinal) || text.Contains("\\u", StringComparison.Ordinal);
            }
            return false;
        }
    }
}
