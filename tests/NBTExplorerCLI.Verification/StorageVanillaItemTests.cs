using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageVanillaItemTests
{
    private static readonly SourceAddress Address = new(new(World, "item-controls.mcfunction", ArtifactKind.McFunction),
        TypedPath.Root, "command.function");
    private const int TaggedItemVersion = 3465;

    internal static void ItemTagMetadata()
    {
        const string script = """
            data modify storage demo:items stack.tag.Enchantments append value {id:"minecraft:sharpness",lvl:1s}
            data modify storage demo:items stack.tag.StoredEnchantments prepend value {id:"minecraft:mending",lvl:1s}
            data modify storage demo:items stack.tag.AttributeModifiers append value {AttributeName:"minecraft:generic.attack_damage",Name:"minecraft:generic.attack_damage",Amount:1,Operation:0,UUID:[I;1,2,3,4],Slot:"mainhand"}
            execute if data storage demo:items stack.tag.AttributeModifiers[{Slot:"mainhand"}] run tellraw @a {"text":"Equipped"}
            data remove storage demo:items stack.tag.AttributeModifiers[{Name:"minecraft:generic.attack_damage",Operation:0}]
            data modify storage demo:items stack.tag.AttributeModifiers[{AttributeName:"generic.armor"}].AttributeName set value "minecraft:generic.armor"
            data modify storage demo:items stack.tag.AttributeModifiers[0].Name set value "minecraft:generic.armor"
            data modify storage demo:items stack.tag.AttributeModifiers[-1].Slot set value "offhand"
            """;
        var uses = new List<CommandStorageAccess>();
        var result = Parse(script, uses.Add);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Equipped", "standard item metadata does not block adjacent visible text");
        Require(uses.Count == 8 && uses.All(use => use.Use == CommandStorageUse.Other), "known item metadata does not authorize display inference");

        foreach (var (path, operation) in new[]
        {
            ("stack.extra.Enchantments", "append value {id:'minecraft:sharpness',lvl:1s}"),
            ("stack.tag.Enchantments", "append value {id:'minecraft:sharpness',lvl:1s,Note:'Readable'}"),
            ("stack.tag.Trim", "set value {material:'minecraft:lapis',pattern:'Custom text'}"),
            ("stack.tag.AttributeModifiers", "append value {Name:'Custom text',Amount:1}"),
            ("stack.tag.AttributeModifiers[0].Name", "set value 'Custom text'"),
            ("stack.tag.AttributeModifiers[0].Caption", "set value 'VisibleText'"),
            ("stack.tag.AttributeModifiers[0].Slot", "set value 'unknown'"),
        })
        {
            var unresolved = Parse($"data modify storage demo:items {path} {operation}");
            Require(!unresolved.Complete && unresolved.Text.Occurrences.IsEmpty, "undetermined locations or payloads stay unresolved");
        }
        foreach (var mode in new[] { "success", "result" })
        {
            var observed = Parse($"""execute store {mode} score $x demo run data modify storage demo:items stack.tag.AttributeModifiers[0].Name set value "minecraft:generic.armor" """.TrimEnd());
            Require(!observed.Complete && observed.Text.Occurrences.IsEmpty,
                "observed attribute assignments retain their unresolved result semantics");
        }
    }

    internal static void InventoryPaths()
    {
        var commands = new[]
        {
            ("data modify storage demo:queue rows append from storage demo:items carried.items[{Slot:-106b}]", 2),
            ("""data modify storage demo:queue rows prepend from storage demo:items carried.items[{id:"minecraft:paper",Slot:2b}]""", 2),
            ("""data modify storage demo:queue rows insert 0 from storage demo:items carried.items[{tag:{Enchantments:[{id:"minecraft:binding_curse"}]}}]""", 2),
            ("data modify block 6 65 0 Items set from storage demo:queue rows", 1),
            ("""data modify storage demo:items carried.items[{Slot:3b,tag:{Enchantments:[{id:"minecraft:binding_curse"}]}}].Slot set value 36b""", 1),
        };
        foreach (var (command, count) in commands)
        {
            var uses = new List<CommandStorageAccess>();
            var result = Parse(command, uses.Add);
            Require(result.Complete && result.Text.Occurrences.IsEmpty && uses.Count == count && uses.All(use => use.Use == CommandStorageUse.Other),
                $"typed inventory transfer does not rewrite a storage value or hide the opaque access: {command}");
        }
        foreach (var command in new[]
        {
            """data modify storage demo:queue rows append from storage demo:items carried.items[{Slot:2b,tag:{display:{Name:'{"text":"Welcome"}'}}}]""",
            "data modify storage demo:queue rows append from storage demo:items carried.items[{Slot:2}]",
            "data modify storage demo:queue rows append from storage demo:items carried.items[{Slot:2b,Slot:3b}]",
            "data modify block 6 65 0 CustomName set from storage demo:queue rows",
            "data modify block 6 65 0 Items set from storage demo:queue rows[0]",
            "execute store success score $x demo run data modify block 6 65 0 Items set from storage demo:queue rows",
        })
            Require(!Parse(command).Complete, $"unverified comparison, type, destination, or result remains unresolved: {command}");
    }

    internal static void ResourceBackedPresentation()
    {
        const string resource = """data modify storage demo:labels catalog append value '{"translate":"enchantment.minecraft.sharpness","color":"gray","italic":false}'""";
        const string level = """data modify storage demo:labels catalog prepend value '[{"translate":"enchantment.minecraft.mending"},{"text":" IV"}]'""";
        foreach (var command in new[] { resource, level })
        {
            var uses = new List<CommandStorageAccess>();
            var result = Parse(command, uses.Add);
            Require(result.Complete && result.Text.Occurrences.IsEmpty
                && uses.Single().Use == CommandStorageUse.ListElementWrite,
                "known resource-backed text without free prose can stay unchanged and keeps its storage observation");
        }

        foreach (var command in new[]
        {
            """data modify storage demo:labels catalog append value '{"translate":"map.title","fallback":"Readable title"}'""",
            """data modify storage demo:labels catalog append value '[{"translate":"map.title"},{"text":" Visible phrase"}]'""",
            """data modify storage demo:labels catalog append value '{"translate":"map.title","with":[{"text":"Readable value"}]}'""",
            """data modify storage demo:labels catalog append value '{"translate":"map.title","font":"demo:glyphs"}'""",
            """data modify storage demo:labels catalog append value '{"translate":"map.title","clickEvent":{"action":"run_command","value":"/say Demo"}}'""",
            "execute store success score $value demo run " + resource,
        })
            Require(!Parse(command).Complete, "prose, reference behaviors and result-observed writes still require use evidence");
    }

    private static CommandExtractionResult Parse(string command, Action<CommandStorageAccess>? uses = null)
        => CommandTextExtractor.Extract(new(Address, command), MinecraftProfiles.ForDataVersion(TaggedItemVersion)!,
            storageAccess: uses, runtimeDataVersion: TaggedItemVersion);
}
