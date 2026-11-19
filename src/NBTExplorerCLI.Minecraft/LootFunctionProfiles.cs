using NBTExplorerCLI.Domain;
using System.Collections.Frozen;

namespace NBTExplorerCLI.Minecraft;

public static class LootFunctionProfiles
{
    private static readonly string[] Common = "set_count enchant_with_levels enchant_randomly set_enchantments furnace_smelt set_damage set_attributes set_name exploration_map set_stew_effect copy_name set_contents limit_count apply_bonus set_loot_table explosion_decay set_lore fill_player_head copy_state set_banner_pattern set_potion set_instrument reference sequence".Split(' ');
    private static readonly FrozenSet<string> NbtTags = Common.Concat("set_nbt looting_enchant copy_nbt".Split(' ')).ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Components = Common.Concat("set_item set_custom_data set_components enchanted_count_increase modify_contents filtered copy_custom_data copy_components set_fireworks set_firework_explosion set_book_cover set_written_book_pages set_writable_book_pages toggle_tooltips set_ominous_bottle_amplifier set_custom_model_data".Split(' ')).ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Snapshot = Components.Concat("set_random_dyes set_random_potion discard".Split(' ')).ToFrozenSet(StringComparer.Ordinal);
    public static bool IsKnown(CapabilityProfile profile, string identifier)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(identifier);
        if (!identifier.Contains(':')) identifier = "minecraft:" + identifier;
        if (!identifier.StartsWith("minecraft:", StringComparison.Ordinal)) return false;
        var name = identifier[10..];
        return profile.Features.GetValueOrDefault(ProfileFacet.ItemSchema)?.Schema switch
        {
            "java-item-tags-1.20.4" when profile.Id is "java-1.20.1" or "java-1.20.2" or "java-1.20.4" => NbtTags.Contains(name),
            "java-item-components-1.20.5" or "java-item-components-1.21.5" => Components.Contains(name),
            "java-item-components-1.21.11" => name == "discard" || Components.Contains(name),
            "java-item-components-26.1-snapshot-6" => Snapshot.Contains(name),
            _ => false
        };
    }
}
