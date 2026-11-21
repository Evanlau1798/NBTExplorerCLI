using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static partial class PackTextExtractor
{
    public static string? StorageFontId(WorldSourceLocation source)
    {
        if (source.PackKind != PackSourceKind.ResourcePack || source.Role != WorldSourceRole.PackJson) return null;
        var entry = Entry(source);
        if (entry.Equals("assets/minecraft/font/default.json", StringComparison.OrdinalIgnoreCase)) return "minecraft:default";
        if (entry.Split('/', 4) is not ["assets", var space, "font", var file] || !file.EndsWith(".json", StringComparison.Ordinal)) return null;
        var id = space + ":" + file[..^5];
        return MinecraftIdentifier.ResourceLocation(id) ? id : null;
    }

    public static HashSet<Rune>? ReadStorageFontGlyphs(byte[] json, DecodeLimits limits, Func<string, bool>? hasTexture = null)
    {
        using var document = BoundedJson.ReadDecoded(json, limits);
        var glyphs = new HashSet<Rune>();
        return FontProviders(document.RootElement, ReadProvider) ? glyphs : null;

        bool ReadProvider(JsonElement provider)
        {
            if (!KnownGlyphProvider(provider)) return false;
            if (provider.TryGetNativeProperty("advances", out var advances))
                return Fields(advances, (glyph, _) => Rune.TryGetRuneAt(glyph, 0, out var scalar)
                    && scalar.Utf16SequenceLength == glyph.Length && Add(scalar));
            var available = true;
            if (hasTexture is not null)
            {
                if (!provider.TryGetNativeProperty("file", out var file)) return false;
                var parts = MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(file)).Split(':', 2);
                available = hasTexture($"assets/{parts[0]}/textures/{parts[1]}");
            }
            return provider.TryGetNativeProperty("chars", out var chars) && ArrayOf(chars, row =>
            {
                // Vanilla bitmap grids reserve zero for an empty cell.
                foreach (var scalar in NativeJsonStrings.Read(row).EnumerateRunes())
                    if (scalar.Value != 0 && !Add(scalar, available)) return false;
                return true;
            });
        }

        bool Add(Rune glyph, bool available = true)
        {
            if (glyph == Rune.ReplacementChar || glyph.Value == 0) return false;
            if (available) glyphs.Add(glyph);
            return true;
        }
    }

    public static bool HasInertStorageShape(WorldSourceLocation source, byte[] json, PackMetadata packMetadata, DecodeLimits limits)
    {
        var metadata = source.Role == WorldSourceRole.PackMetadata && Entry(source) == "pack.mcmeta";
        var language = source.PackKind == PackSourceKind.ResourcePack && source.Role == WorldSourceRole.Language;
        var font = StorageFontId(source) is not null;
        var profile = source.Role == WorldSourceRole.PackJson && source.PackKind == PackSourceKind.Datapack
            ? MinecraftProfiles.ForPackMetadata(packMetadata) : null;
        var plural = profile?.Features[ProfileFacet.PackLayout].Schema == "java-pack-plural";
        var kind = profile is not null && Entry(source).Split('/', 4) is ["data", _, var directory, var file]
            && file.EndsWith(".json", StringComparison.Ordinal) ? directory : null;
        var predicate = kind == (plural ? "predicates" : "predicate");
        var advancement = kind == (plural ? "advancements" : "advancement");
        if (!metadata && !language && !font && !predicate && !advancement) return false;
        using var document = BoundedJson.ReadDecoded(json, limits);
        var root = document.RootElement;
        if (predicate || advancement) return KnownStaticPredicate(root, profile!, limits, advancement ? source : null);
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
        return FontProviders(root, KnownGlyphProvider);
    }

    private static bool FontProviders(JsonElement root, Func<JsonElement, bool> provider)
        => Fields(root, (key, _) => key == "providers") && root.TryGetNativeProperty("providers", out var providers)
            && ArrayOf(providers, provider);

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
