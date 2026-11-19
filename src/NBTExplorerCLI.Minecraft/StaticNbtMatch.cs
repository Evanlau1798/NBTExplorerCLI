using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

internal static class StaticNbtMatch
{
    internal static bool Entity(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(field => !field.Node.ContainsStringValues || field.Selector.Key switch
        {
            "Tags" => field.Node.Value is LogicalMinecraftList tags && tags.Items.All(item => item is LogicalMinecraftLeaf { Value: NbtString }),
            "Item" or "SelectedItem" => ItemStack(field.Node),
            "Inventory" or "EnderItems" or "ArmorItems" or "HandItems" => field.Node.Value is LogicalMinecraftList
                && field.Node.Children.All(item => ItemStack(item.Node)),
            _ => false
        });

    internal static bool ItemStack(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(field => !field.Node.ContainsStringValues || field.Selector.Key switch
        {
            "id" => ResourceId(field.Node),
            "tag" => ItemTag(field.Node),
            _ => false
        });

    private static bool ItemTag(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(field => !field.Node.ContainsStringValues || field.Selector.Key switch
        {
            "Potion" => ResourceId(field.Node),
            "Enchantments" or "StoredEnchantments" => field.Node.Value is LogicalMinecraftList
                && field.Node.Children.All(item => Enchantment(item.Node)),
            _ => false
        });

    internal static bool Enchantment(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(member => !member.Node.ContainsStringValues || member.Selector.Key == "id" && ResourceId(member.Node));

    private static bool ResourceId(SnbtNode node) => node.Value is LogicalMinecraftLeaf { Value: NbtString id }
        && MinecraftIdentifier.ResourceLocation(id.Value);
}
