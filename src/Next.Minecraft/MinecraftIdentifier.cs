namespace Next.Minecraft;

internal static class MinecraftIdentifier
{
    internal static string DefaultNamespace(string value)
    {
        var separator = value.IndexOf(':');
        return separator < 0 ? "minecraft:" + value : separator == 0 ? "minecraft" + value : value;
    }
}
