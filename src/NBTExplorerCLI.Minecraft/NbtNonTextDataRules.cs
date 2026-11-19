using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class NbtTextReader
{
    private static readonly HashSet<string> CommonNonTextData = new("raids.dat raids_end.dat random_sequences.dat idcounts.dat chunks.dat".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> NamespacedNonTextData = new("game_rules.dat scheduled_events.dat stopwatches.dat wandering_trader.dat weather.dat world_clocks.dat world_gen_settings.dat chunk_tickets.dat world_border.dat ender_dragon_fight.dat".Split(' '), StringComparer.Ordinal);
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
