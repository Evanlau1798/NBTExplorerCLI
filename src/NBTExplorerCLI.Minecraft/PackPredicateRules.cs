using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static partial class PackTextExtractor
{
    private static readonly UTF8Encoding PredicateUtf8 = new(false, true);

    private static bool KnownStaticPredicate(JsonElement root, CapabilityProfile profile, DecodeLimits limits)
    {
        bool Condition(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(Condition);
            if (!value.TryGetNativeProperty("condition", out var condition) || condition.ValueKind != JsonValueKind.String) return false;
            return MinecraftIdentifier.DefaultNamespace(NativeJsonStrings.Read(condition)) switch
            {
                "minecraft:all_of" or "minecraft:any_of" => value.TryGetNativeProperty("terms", out var terms)
                    && terms.ValueKind == JsonValueKind.Array && terms.EnumerateArray().All(Condition)
                    && Fields(value, (name, _) => name is "condition" or "terms"),
                "minecraft:inverted" => value.TryGetNativeProperty("term", out var term) && Condition(term)
                    && Fields(value, (name, _) => name is "condition" or "term"),
                "minecraft:entity_properties" => value.TryGetNativeProperty("entity", out var entity) && entity.ValueKind == JsonValueKind.String
                    && NativeJsonStrings.Read(entity) is "this" or "killer" or "direct_killer" or "killer_player"
                    && value.TryGetNativeProperty("predicate", out var predicate) && Entity(predicate)
                    && Fields(value, (name, _) => name is "condition" or "entity" or "predicate"),
                "minecraft:location_check" => value.TryGetNativeProperty("predicate", out var location) && Location(location)
                    && Fields(value, (name, member) => name is "condition" or "predicate"
                        || name is "offsetX" or "offsetY" or "offsetZ" && member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out _)),
                _ => false
            };
        }
        bool Entity(JsonElement value) => Fields(value, (name, member) => name switch
        {
            "type" => Identifier(member),
            "distance" or "effects" or "flags" => Numbers(member),
            "location" or "stepping_on" => Location(member),
            "vehicle" or "passenger" or "targeted_entity" => Entity(member),
            "equipment" => Fields(member, (slot, item) => slot is "head" or "chest" or "legs" or "feet" or "mainhand" or "offhand" && Item(item)),
            "nbt" => NbtMatch(member, entityTags: true),
            _ => false
        });
        bool Item(JsonElement value) => Fields(value, (name, member) => name switch
        {
            "items" => Identifiers(member), "tag" or "potion" => Identifier(member),
            "count" or "durability" => Numbers(member), "nbt" => NbtMatch(member),
            _ => false
        });
        bool Location(JsonElement value) => Fields(value, (name, member) => name switch
        {
            "biome" or "biomes" or "dimension" or "structure" or "structures" => Identifiers(member),
            "position" or "light" or "smokey" or "can_see_sky" => Numbers(member),
            "block" => Fields(member, (key, field) => key switch
            {
                "blocks" or "tag" => Identifiers(field), "nbt" => NbtMatch(field),
                "state" => Fields(field, (_, state) => State(state)), _ => false
            }),
            _ => false
        });
        bool NbtMatch(JsonElement value, bool entityTags = false)
        {
            if (value.ValueKind != JsonValueKind.String) return false;
            try
            {
                var parsed = Snbt.ParseDocument(PredicateUtf8.GetBytes(NativeJsonStrings.Read(value)), MinecraftProfiles.SnbtGrammar(profile), limits).Root;
                return entityTags ? StaticNbtMatch.Entity(parsed) : parsed.Value is LogicalMinecraftCompound && !parsed.ContainsStringValues;
            }
            catch (SnbtException error) when (error.Code != SnbtError.LimitExceeded) { return false; }
            catch (EncoderFallbackException) { return false; }
        }
        return Condition(root);
    }

    private static bool Fields(JsonElement value, Func<string, JsonElement, bool> matches) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().All(property => matches(NativeJsonStrings.PropertyName(property), property.Value));
    private static bool Identifier(JsonElement value) => value.ValueKind == JsonValueKind.String
        && CommandTextReader.ResourceOrTag(NativeJsonStrings.Read(value));
    private static bool Identifiers(JsonElement value) => Identifier(value)
        || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(Identifier);
    private static bool Numbers(JsonElement value) => value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
        || Fields(value, (_, member) => Numbers(member));
    private static bool State(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
        || Fields(value, (name, member) => name is "min" or "max" && State(member));
}
