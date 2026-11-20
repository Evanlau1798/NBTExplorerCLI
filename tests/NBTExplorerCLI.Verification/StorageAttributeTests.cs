using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageAttributeTests
{
    internal static void TransferRoles()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string write = "datapacks/p/data/demo/functions/attributes.mcfunction";
        const string read = "datapacks/p/data/demo/functions/move.mcfunction";
        const string control = """
            data modify storage demo:raw attribute set value "minecraft:generic.armor"
            data modify storage demo:build modifier.Name set from storage demo:raw attribute
            data modify storage demo:build modifier.AttributeName set from storage demo:raw attribute
            execute if score step gate matches 1 run data modify storage demo:build modifier.Slot set value "offhand"
            data modify storage demo:gear stack.tag.AttributeModifiers prepend from storage demo:build modifier
            tellraw @a {"text":"Bienvenue"}
            """;
        const string transfer = """
            data modify storage demo:gear stack set from entity @e[type=armor_stand,limit=1] ArmorItems[3]
            data modify storage demo:gear stack.tag.AttributeModifiers append value {AttributeName:"minecraft:generic.armor",Name:"minecraft:generic.armor",Amount:1d,Operation:0,UUID:[I;1,2,3,4],Slot:"mainhand"}
            data modify storage demo:relay selected append from storage demo:gear stack.tag.AttributeModifiers[{Slot:"mainhand"}]
            data modify storage demo:relay filtered append from storage demo:relay selected[{AttributeName:"minecraft:generic.armor"}]
            execute store success score step gate run data modify storage demo:relay first set from storage demo:relay filtered[0]
            """;
        try
        {
            Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(world, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
            Put(world, read, Encoding.UTF8.GetBytes(transfer));
            Put(world, write, Encoding.UTF8.GetBytes(control));
            var snapshot = WorldQueryTests.Query(world, cache);
            Require(snapshot.Manifest.CoverageComplete, "storage attribute identifiers, compound fields, and filtered movement have a shared native role");
            Require(snapshot.ReadSources().Where(row => row.Source.Location.Artifact.RelativePath is write or read)
                .SelectMany(row => row.Text?.Occurrences ?? []).Select(text => text.Text).SequenceEqual(["Bienvenue"]),
                "only the visible message is offered for translation");
            foreach (var unsupported in new[]
            {
                control.Replace("generic.armor", "visible text", StringComparison.Ordinal),
                control.Replace("offhand", "left glove", StringComparison.Ordinal),
                control.Replace("stack.tag.AttributeModifiers", "stack.tag.UnknownFields", StringComparison.Ordinal),
                control + "\n" + """tellraw @a {"storage":"demo:raw","nbt":"attribute","interpret":false}""",
            })
            {
                Put(world, write, Encoding.UTF8.GetBytes(unsupported));
                Require(!WorldQueryTests.Query(world, cache).Manifest.CoverageComplete, "unproven names, slots, paths, and visible reuse stay unresolved");
            }
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }
}
