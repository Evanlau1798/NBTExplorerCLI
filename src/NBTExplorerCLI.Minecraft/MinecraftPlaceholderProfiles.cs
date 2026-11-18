using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Minecraft;

public static class MinecraftPlaceholderProfiles
{
    public static PlaceholderFormat ForRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return role == "language.value" ? PlaceholderFormat.NormalizedPercent
            : role.EndsWith(".fallback", StringComparison.Ordinal) ? PlaceholderFormat.Percent : PlaceholderFormat.Literal;
    }
}
