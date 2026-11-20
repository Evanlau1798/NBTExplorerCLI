using System.Text.Json;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static partial class PackTextExtractor
{
    private static bool KnownWorldgen(string kind, JsonElement value) => kind switch
    {
        "dimension" => value.TryGetNativeProperty("type", out var type) && (WorldgenId(type) || DimensionType(type))
            && value.TryGetNativeProperty("generator", out var generator) && WorldGenerator(generator)
            && Fields(value, (name, _) => name is "type" or "generator"),
        "dimension_type" => DimensionType(value),
        "worldgen" => Biome(value),
        _ => false
    };

    private static bool WorldGenerator(JsonElement value)
    {
        if (!value.TryGetNativeProperty("type", out var type) || !WorldgenId(type)) return false;
        return MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(type)) switch
        {
            "minecraft:debug" => Fields(value, (name, _) => name == "type"),
            "minecraft:flat" => value.TryGetNativeProperty("settings", out var flat) && FlatSettings(flat)
                && Fields(value, (name, _) => name is "type" or "settings"),
            "minecraft:noise" => value.TryGetNativeProperty("settings", out var noise) && WorldgenId(noise)
                && value.TryGetNativeProperty("biome_source", out var source) && BiomeSource(source)
                && Fields(value, (name, _) => name is "type" or "settings" or "biome_source"),
            _ => false
        };
    }

    private static bool FlatSettings(JsonElement value) => value.TryGetNativeProperty("layers", out var layers)
        && ArrayOf(layers, layer => layer.TryGetNativeProperty("block", out var block) && WorldgenId(block)
            && layer.TryGetNativeProperty("height", out var height) && IntegerValue(height)
            && Fields(layer, (name, _) => name is "block" or "height"))
        && Fields(value, (name, member) => name switch
        {
            "layers" => true, "biome" => WorldgenId(member), "lakes" or "features" => BooleanValue(member),
            "structure_overrides" => Identifiers(member), _ => false
        });

    private static bool BiomeSource(JsonElement value)
    {
        if (!value.TryGetNativeProperty("type", out var type) || !WorldgenId(type)) return false;
        var member = MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(type)) switch
        {
            "minecraft:fixed" => "biome", "minecraft:multi_noise" => "preset", _ => null
        };
        return member is not null && value.TryGetNativeProperty(member, out var definition) && WorldgenId(definition)
            && Fields(value, (name, _) => name == "type" || name == member);
    }

    private static bool DimensionType(JsonElement value) => value.TryGetNativeProperty("logical_height", out var logicalHeight)
        && IntegerValue(logicalHeight) && Fields(value, (name, member) => name switch
        {
            "ultrawarm" or "natural" or "piglin_safe" or "respawn_anchor_works" or "bed_works" or "has_raids"
                or "has_skylight" or "has_ceiling" => BooleanValue(member),
            "coordinate_scale" or "ambient_light" => NumberValue(member),
            "min_y" or "height" or "logical_height" or "monster_spawn_block_light_limit" => IntegerValue(member),
            "fixed_time" => member.ValueKind == JsonValueKind.Number && member.TryGetInt64(out _),
            "effects" => WorldgenId(member), "infiniburn" => Identifier(member),
            "monster_spawn_light_level" => IntegerValue(member) || UniformLight(member), _ => false
        });

    private static bool UniformLight(JsonElement value) => value.TryGetNativeProperty("type", out var type)
        && WorldgenId(type) && MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(type)) == "minecraft:uniform"
        && (value.TryGetNativeProperty("value", out var range)
            ? LightRange(range) && Fields(range, (name, _) => name is "min_inclusive" or "max_inclusive")
                && Fields(value, (name, _) => name is "type" or "value")
            : LightRange(value) && Fields(value, (name, _) => name is "type" or "min_inclusive" or "max_inclusive"));

    private static bool LightRange(JsonElement value) => value.TryGetNativeProperty("min_inclusive", out var min) && IntegerValue(min)
        && value.TryGetNativeProperty("max_inclusive", out var max) && IntegerValue(max);

    private static bool Biome(JsonElement value) => value.TryGetNativeProperty("effects", out var effects)
        && BiomeEffects(effects) && Fields(value, (name, member) => name switch
        {
            "effects" => true, "temperature" or "downfall" or "depth" or "scale" => NumberValue(member),
            "has_precipitation" => BooleanValue(member), "temperature_modifier" or "precipitation" or "category" => WorldgenId(member),
            // Vanilla ignores this misplaced sound setting; preserve only its recognized, non-text shape.
            "additions_sound" => BiomeAdditionsSound(member),
            "carvers" => Identifiers(member) || Fields(member, (key, carvers) => key is "air" or "liquid" && Identifiers(carvers)),
            "features" => ArrayOf(member, Identifiers),
            "spawners" => Fields(member, (_, spawns) => ArrayOf(spawns, spawn =>
                spawn.TryGetNativeProperty("type", out var type) && WorldgenId(type)
                && Fields(spawn, (key, field) => key == "type" || key is "weight" or "minCount" or "maxCount" && IntegerValue(field)))),
            "spawn_costs" => Fields(member, (id, cost) => MinecraftIdentifier.ResourceLocation(id)
                && Fields(cost, (key, field) => key is "energy_budget" or "charge" && NumberValue(field))),
            _ => false
        });

    private static bool BiomeEffects(JsonElement value) => Fields(value, (name, member) => name switch
    {
        "sky_color" or "fog_color" or "water_color" or "water_fog_color" or "foliage_color" or "grass_color" => IntegerValue(member),
        "grass_color_modifier" => WorldgenId(member), "ambient_sound" => WorldgenSound(member),
        "music" => SoundSettings(member, (key, field) => key is "min_delay" or "max_delay" && IntegerValue(field)
            || key == "replace_current_music" && BooleanValue(field)),
        "mood_sound" => SoundSettings(member, (key, field) => key is "tick_delay" or "block_search_extent" && IntegerValue(field)
            || key == "offset" && NumberValue(field)),
        "additions_sound" => BiomeAdditionsSound(member),
        "particle" => member.TryGetNativeProperty("options", out var options) && options.TryGetNativeProperty("type", out var type)
            && WorldgenId(type) && Fields(options, (key, _) => key == "type")
            && Fields(member, (key, field) => key == "options" || key == "probability" && NumberValue(field)),
        _ => false
    });

    private static bool BiomeAdditionsSound(JsonElement value) => SoundSettings(value, (key, field) => key == "tick_chance" && NumberValue(field));

    private static bool SoundSettings(JsonElement value, Func<string, JsonElement, bool> option) => value.TryGetNativeProperty("sound", out var sound)
        && WorldgenSound(sound) && Fields(value, (name, member) => name == "sound" || option(name, member));
    private static bool WorldgenSound(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var sound = NativeJsonStrings.Read(value);
            return sound.Length == 0 || MinecraftIdentifier.ResourceLocation(sound);
        }
        return value.TryGetNativeProperty("sound_id", out var id) && WorldgenId(id)
            && Fields(value, (name, member) => name == "sound_id" || name == "range" && NumberValue(member));
    }
    private static bool WorldgenId(JsonElement value) => value.ValueKind == JsonValueKind.String
        && MinecraftIdentifier.ResourceLocation(NativeJsonStrings.Read(value));
    private static bool ArrayOf(JsonElement value, Func<JsonElement, bool> element) => value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().All(element);
    private static bool NumberValue(JsonElement value) => value.ValueKind == JsonValueKind.Number;
    private static bool IntegerValue(JsonElement value) => NumberValue(value) && value.TryGetInt32(out _);
    private static bool BooleanValue(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False;
}
