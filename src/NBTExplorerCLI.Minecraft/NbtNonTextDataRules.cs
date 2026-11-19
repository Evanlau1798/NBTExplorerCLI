using System.Globalization;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class NbtTextReader
{
    private static readonly HashSet<string> CommonNonTextData = new("raids.dat raids_end.dat random_sequences.dat idcounts.dat chunks.dat".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> NamespacedNonTextData = new("game_rules.dat scheduled_events.dat stopwatches.dat wandering_trader.dat weather.dat world_clocks.dat world_gen_settings.dat chunk_tickets.dat world_border.dat ender_dragon_fight.dat".Split(' '), StringComparer.Ordinal);
    private bool ClassicNonTextData(string name, NbtValue data, TypedPath path)
    {
        if (name.StartsWith("map_", StringComparison.Ordinal) && name.EndsWith(".dat", StringComparison.Ordinal) && name.Length > 8)
        {
            var id = name.AsSpan(4, name.Length - 8);
            return int.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                && id.SequenceEqual(number.ToString(CultureInfo.InvariantCulture))
                && Field(data, "colors", path) is NbtByteArray
                && Field(data, "width", path) is NbtShort && Field(data, "height", path) is NbtShort;
        }
        if (name is "Mineshaft.dat" or "Stronghold.dat" or "Temple.dat" or "Fortress.dat" or "Village.dat")
            return Field(data, "Features", path) is NbtCompound;
        return name is "villages.dat" or "villages_end.dat" or "villages_nether.dat"
            && Field(data, "Villages", path) is NbtList && Field(data, "Tick", path) is NbtInt;
    }
    private bool KnownNonTextData(string artifact, string filename)
    {
        var java12111Settings = layoutProfile.Id == "java-1.21.11" && filename is "stopwatches.dat" or "world_border.dat";
        if (!java12111Settings && !CommonNonTextData.Contains(filename)
            && !(layoutProfile.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced" && NamespacedNonTextData.Contains(filename))) return false;
        return VanillaSavedDataPath(artifact, filename);
    }
    private bool VanillaSavedDataPath(string artifact, string filename)
    {
        var namespaced = layoutProfile.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced";
        var directories = new List<string>();
        if (binding.Dimension is null) directories.Add("");
        else
        {
            var fields = binding.Dimension.Split(':'); if (fields.Length != 2) return false;
            directories.Add("dimensions/" + fields[0] + "/" + fields[1]);
            if (!namespaced)
            {
                if (binding.Dimension == "minecraft:overworld") directories.Add("");
                else if (binding.Dimension == "minecraft:the_nether") directories.Add("DIM-1");
                else if (binding.Dimension == "minecraft:the_end") directories.Add("DIM1");
            }
        }
        return directories.Any(directory => artifact == (directory.Length == 0 ? "" : directory + "/") + (namespaced ? "data/minecraft/" : "data/") + filename);
    }
}
