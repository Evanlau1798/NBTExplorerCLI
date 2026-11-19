using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using System.Text.Json;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

public static partial class PackTextExtractor
{
    public static TextExtractionResult Extract(WorldSourceLocation source, byte[] json, PackMetadata metadata, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(metadata);
        if (source.PackKind != metadata.Kind || source.PackRoot is null) throw new ArgumentException("Requires exact pack identity and metadata", nameof(source));
        var profile = MinecraftProfiles.ForPackMetadata(metadata) ?? throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown pack text schema");
        var entry = Entry(source);
        limits ??= DecodeLimits.Default;
        using var document = BoundedJson.ReadDecoded(json, limits);
        var root = document.RootElement;
        var binding = new SourceAddress(source.Artifact, TypedPath.Root, "pack.text", source.Dimension);
        var output = new TextExtractionCollector(binding, false, limits, scope) { ComponentRoot = componentRoot };
        if (source.Role == WorldSourceRole.Language && metadata.Kind == PackSourceKind.ResourcePack)
        {
            if (root.ValueKind != JsonValueKind.Object) output.Diagnostic(TypedPath.Root, "InvalidLanguageObject");
            else foreach (var property in root.EnumerateObject())
            {
                var key = NativeJsonStrings.PropertyName(property); var path = At(TypedPath.Root, key);
                if (property.Value.ValueKind != JsonValueKind.String) { output.Diagnostic(path, "InvalidLanguageValue"); continue; }
                output.Literal(path, NativeJsonStrings.Read(property.Value), "language.value");
                output.Key(path, key, "language.key", propertyName: true);
            }
        }
        else if (source.Role == WorldSourceRole.PackMetadata && entry == "pack.mcmeta")
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetNativeProperty("pack", out var pack) && pack.ValueKind == JsonValueKind.Object && pack.TryGetNativeProperty("description", out var description))
                Component(description, new([PathSegment.ForKey("pack"), PathSegment.ForKey("description")]), "pack.description");
            else output.Diagnostic(TypedPath.Root, "InvalidPackMetadata");
        }
        else if (source.Role == WorldSourceRole.PackJson && metadata.Kind == PackSourceKind.Datapack)
        {
            var parts = entry.Split('/');
            var directory = profile.Features[ProfileFacet.PackLayout].Schema == "java-pack-plural" ? "advancements" : "advancement";
            var plural = profile.Features[ProfileFacet.PackLayout].Schema == "java-pack-plural";
            if (parts.Length >= 4 && parts[0] == "data" && parts[2] == directory && parts[^1].EndsWith(".json", StringComparison.Ordinal))
            {
                if (root.ValueKind != JsonValueKind.Object) output.Diagnostic(TypedPath.Root, "InvalidAdvancement");
                else if (root.TryGetNativeProperty("display", out var display))
                {
                    if (display.ValueKind != JsonValueKind.Object) output.Diagnostic(At(TypedPath.Root, "display"), "InvalidAdvancement");
                    else foreach (var field in new[] { "title", "description" })
                        if (display.TryGetNativeProperty(field, out var component)) Component(component, new([PathSegment.ForKey("display"), PathSegment.ForKey(field)]), "advancement." + field);
                }
            }
            else if (parts.Length >= 4 && parts[0] == "data" && parts[2] == (plural ? "loot_tables" : "loot_table")) Loot(root, TypedPath.Root, output, Component, profile, RawComponent);
            else if (parts.Length >= 4 && parts[0] == "data" && parts[2] == (plural ? "item_modifiers" : "item_modifier")) Modifier(root, TypedPath.Root, output, Component, profile, RawComponent);
            else if (parts.Length >= 4 && parts[0] == "data" && parts[2] == (plural ? "predicates" : "predicate"))
            {
                if (!KnownStaticPredicate(root, profile, limits)) output.Diagnostic(TypedPath.Root, "UnsupportedPredicateSchema");
            }
            else if (parts.Length >= 4 && parts[0] == "data" && parts[2] == (plural ? "recipes" : "recipe") && parts[^1].EndsWith(".json", StringComparison.Ordinal))
            {
                var resultPath = At(TypedPath.Root, "result");
                if (!root.TryGetNativeProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(type)) is not ("minecraft:crafting_shaped" or "minecraft:crafting_shapeless"))
                    output.Diagnostic(TypedPath.Root, "UnsupportedPackRecipe");
                else
                {
                    var usesItemField = profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4";
                    root.TryGetNativeProperty("result", out var result);
                    var id = result.ValueKind == JsonValueKind.String && profile.Id == "java-26.1-snapshot-6" ? result
                        : result.TryGetNativeProperty(usesItemField ? "item" : "id", out var field) ? field : default;
                    var item = id.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(id) : "";
                    if (!MinecraftIdentifier.ResourceLocation(item) || MinecraftIdentifier.DefaultNamespace(item) == "minecraft:air")
                        output.Diagnostic(resultPath, "InvalidRecipeResult");
                    else if (!usesItemField && result.ValueKind == JsonValueKind.Object)
                        JsonItem(result, resultPath, output, Component, RawComponent, profile);
                }
            }
            else if (parts.Length >= 4 && parts[0] == "data" && profile.Id == "java-1.21.11" && parts[2] == "enchantment")
            {
                if (root.TryGetNativeProperty("description", out var description))
                    Component(description, At(TypedPath.Root, "description"), "enchantment.description");
                else output.Diagnostic(TypedPath.Root, "InvalidEnchantmentDescription");
                if (root.TryGetNativeProperty("effects", out var effects) && (effects.ValueKind != JsonValueKind.Object || effects.EnumerateObject().Any()))
                    output.Diagnostic(At(TypedPath.Root, "effects"), "UnsupportedEnchantmentEffects");
            }
            else if (parts.Length < 4 || parts[0] != "data" || parts[2] != "tags") output.Diagnostic(TypedPath.Root, "UnsupportedPackJson");
        }
        else if (profile.Id == "java-1.20.1" && metadata.Kind == PackSourceKind.ResourcePack && source.Role == WorldSourceRole.PackJson
            && entry.Split('/', 4) is ["assets", _, "models" or "font" or "shaders", _])
        {
            if (root.ValueKind != JsonValueKind.Object) output.Diagnostic(TypedPath.Root, "InvalidResourceDefinition");
        }
        else output.Diagnostic(TypedPath.Root, "UnsupportedPackSource");
        return output.Finish();

        void Component(JsonElement value, TypedPath path, string role)
        {
            if (!output.CouldInclude(role)) return;
            var address = new SourceAddress(source.Artifact, path, role, source.Dimension);
            output.Merge(TextComponentExtractor.Json(address, Encoding.UTF8.GetBytes(value.GetRawText()), profile, limits, output.Scope, output.ComponentRoot is null ? null : output.RegisterComponent));
        }
        void RawComponent(JsonElement value, TypedPath path, string role)
        {
            if (!output.CouldInclude(role)) return;
            if (profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt") { Component(value, path, role); return; }
            if (value.ValueKind != JsonValueKind.String) { output.Diagnostic(path, "InvalidJsonComponentString"); return; }
            var address = new SourceAddress(source.Artifact, path, role, source.Dimension);
            output.Merge(TextComponentExtractor.EmbeddedJsonString(address, NativeJsonStrings.Read(value), profile, limits, output.Scope, output.ComponentRoot is null ? null : output.RegisterComponent));
        }
    }
    private static string Entry(WorldSourceLocation source)
    {
        if (source.Artifact.ContainerEntry is { } entry && source.Artifact.RelativePath == source.PackRoot) return entry;
        var prefix = source.PackRoot + "/";
        return source.Artifact.ContainerEntry is null && source.Artifact.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
            ? source.Artifact.RelativePath[prefix.Length..] : throw new ArgumentException("Source is outside its literal pack root", nameof(source));
    }
}
