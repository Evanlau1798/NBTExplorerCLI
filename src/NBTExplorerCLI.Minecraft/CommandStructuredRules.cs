using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private void Structured(int start, int end, TypedPath path, string identity, bool block, bool summonTemplate = false)
    {
        if (!identity.StartsWith("minecraft:", StringComparison.Ordinal)) { Unknown(path, start, end - start, "UnknownCommandNbtSchema"); return; }
        var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, block ? "block.data" : "entity.data", source.Address.Dimension, source.Address.Chunk);
        var callback = ComponentCallback(path, start, true);
        var result = block ? SnbtTextExtractor.BlockEntity(binding, source.Command[start..end], profile, limits, scope, identity, callback)
            : SnbtTextExtractor.Entity(binding, source.Command[start..end], profile, limits, scope, identity, callback);
        MergeComponent(result, path, start, end, true);
        if (result.Diagnostics.IsEmpty) ComponentReferences(Utf8.GetBytes(source.Command[start..end]), start, end, path, true, itemTree: true);
        if (summonTemplate && nameTemplate is not null && result.Diagnostics.IsEmpty) ReadSummonNameTemplate(start, end, path, identity, result);
    }
    private void BlockArgument(CommandToken token, TypedPath path)
    {
        var brace = token.Text.IndexOf('{'); if (brace < 0) return;
        var state = token.Text.IndexOf('['); var name = MinecraftIdentifier.DefaultNamespace(token.Text[..(state >= 0 && state < brace ? state : brace)]);
        if (!name.StartsWith("minecraft:", StringComparison.Ordinal)) { Unknown(path, token.Start + brace, token.Text.Length - brace, "UnknownCommandBlockSchema"); return; }
        var block = name[10..];
        var wood = new[] { "oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "cherry", "bamboo", "crimson", "warped", "pale_oak" };
        var identity = wood.Any(value => block == value + "_hanging_sign" || block == value + "_wall_hanging_sign") ? "hanging_sign"
            : wood.Any(value => block == value + "_sign" || block == value + "_wall_sign") ? "sign"
            : block is "command_block" or "chain_command_block" or "repeating_command_block" ? "command_block"
            : block is "chest" or "barrel" or "furnace" or "blast_furnace" or "smoker" or "hopper" or "dispenser" or "dropper" or "lectern" or "brewing_stand" ? block : null;
        if (identity is null) { Unknown(path, token.Start + brace, token.Text.Length - brace, "UnknownCommandBlockSchema"); return; }
        Structured(token.Start + brace, token.Start + token.Text.Length, At(path, "nbt"), "minecraft:" + identity, true);
    }
}
