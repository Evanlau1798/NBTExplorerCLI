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

    internal static bool ItemTag(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(field => !field.Node.ContainsStringValues || field.Selector.Key switch
        {
            "Potion" => ResourceId(field.Node),
            "Enchantments" or "StoredEnchantments" => field.Node.Value is LogicalMinecraftList
                && field.Node.Children.All(item => Enchantment(item.Node)),
            _ => false
        });

    internal static bool Enchantment(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(member => !member.Node.ContainsStringValues || member.Selector.Key == "id" && ResourceId(member.Node));

    internal static bool AttributeModifier(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && root.Children.All(member => !member.Node.ContainsStringValues || AttributeModifierField(member.Selector.Key, member.Node));

    internal static bool AttributeModifierField(string? field, SnbtNode node) => field switch
    {
        "AttributeName" or "Name" => ResourceId(node),
        "Slot" => node.Value is LogicalMinecraftLeaf { Value: NbtString { Value: "mainhand" or "offhand" or "head" or "chest" or "legs" or "feet" } },
        _ => false
    };

    internal static bool InventoryItemFilter(SnbtNode root) => ExactFields(root)
        && root.Children.Any(field => field.Selector.Key is "Slot" or "tag")
        && root.Children.All(field => field.Selector.Key switch
    {
        "Slot" => field.Node.Value is LogicalMinecraftLeaf { Value: NbtByte },
        "id" => ResourceId(field.Node),
        "tag" => ExactFields(field.Node) && field.Node.Children.All(tag => tag.Selector.Key switch
        {
            "Enchantments" or "StoredEnchantments" => tag.Node.Value is LogicalMinecraftList && !tag.Node.Children.IsEmpty
                && tag.Node.Children.All(entry => ExactFields(entry.Node) && entry.Node.Children.All(member => member.Selector.Key switch
                {
                    "id" => ResourceId(member.Node),
                    "lvl" => member.Node.Value is LogicalMinecraftLeaf { Value: NbtShort },
                    _ => false
                })),
            _ => false
        }),
        _ => false
    });

    private static bool ExactFields(SnbtNode node) => node.Value is LogicalMinecraftCompound && !node.Children.IsEmpty
        && node.Children.Select(child => child.Selector).Distinct().Count() == node.Children.Length;

    internal static bool StorageMetadataFilter(SnbtNode root) => root.Value is LogicalMinecraftCompound
        && (root.Children is [{ Selector.Key: "AttributeName" } attribute] && ResourceId(attribute.Node)
            || root.Children.Length == 2 && root.Children.Any(field => field.Selector.Key == "id" && ResourceId(field.Node))
            && root.Children.Any(field => field.Selector.Key == "Slot" && field.Node.Value is LogicalMinecraftLeaf { Value: NbtByte }));

    internal static bool ArmorTrim(SnbtNode root) => root.Value is LogicalMinecraftCompound && root.Children.Length == 2
        && root.Children.Any(field => field.Selector.Key == "material" && ResourceId(field.Node))
        && root.Children.Any(field => field.Selector.Key == "pattern" && ResourceId(field.Node));

    private static bool ResourceId(SnbtNode node) => node.Value is LogicalMinecraftLeaf { Value: NbtString id }
        && MinecraftIdentifier.ResourceLocation(id.Value);
}
