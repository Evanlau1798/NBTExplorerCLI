using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static partial class PackTextExtractor
{
    public static bool HasInertStorageShape(WorldSourceLocation source, byte[] json, DecodeLimits limits)
    {
        var metadata = source.Role == WorldSourceRole.PackMetadata && Entry(source) == "pack.mcmeta";
        var language = source.PackKind == PackSourceKind.ResourcePack && source.Role == WorldSourceRole.Language;
        var font = source.PackKind == PackSourceKind.ResourcePack && source.Role == WorldSourceRole.PackJson
            && Entry(source).Split('/', 4) is ["assets", _, "font", var name] && name.EndsWith(".json", StringComparison.Ordinal);
        if (!metadata && !language && !font) return false;
        using var document = BoundedJson.ReadDecoded(json, limits);
        var root = document.RootElement;
        if (language) return Fields(root, (_, value) => value.ValueKind == JsonValueKind.String);
        if (metadata)
            return Fields(root, (key, _) => key == "pack") && root.TryGetNativeProperty("pack", out var pack)
                && pack.TryGetNativeProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                && Fields(pack, (key, value) => key switch
                {
                    "description" => value.ValueKind == JsonValueKind.String,
                    "pack_format" or "min_format" or "max_format" => NumberShape(value),
                    "supported_formats" => NumberShape(value) || Fields(value, (endpoint, bound) => endpoint is "min_inclusive" or "max_inclusive" && NumberShape(bound)),
                    _ => false
                });
        return Fields(root, (key, _) => key == "providers") && root.TryGetNativeProperty("providers", out var providers)
            && ArrayOf(providers, KnownGlyphProvider);
    }

    private static bool NumberShape(JsonElement value) => IntegerValue(value) || ArrayOf(value, IntegerValue);

    private static bool KnownGlyphProvider(JsonElement provider)
    {
        if (!provider.TryGetNativeProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
        return NativeJsonStrings.Read(type) switch
        {
            "space" => provider.TryGetNativeProperty("advances", out var advances) && Fields(advances, (_, shift) => NumberValue(shift))
                && Fields(provider, (key, _) => key is "type" or "advances"),
            "bitmap" => provider.TryGetNativeProperty("file", out var file) && WorldgenId(file)
                && provider.TryGetNativeProperty("ascent", out var ascent) && IntegerValue(ascent)
                && provider.TryGetNativeProperty("chars", out var chars) && ArrayOf(chars, row => row.ValueKind == JsonValueKind.String)
                && Fields(provider, (key, value) => key is "type" or "file" or "ascent" or "chars" || key == "height" && IntegerValue(value)),
            _ => false
        };
    }
}
