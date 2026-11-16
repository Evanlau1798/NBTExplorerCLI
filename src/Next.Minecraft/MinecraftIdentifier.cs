namespace Next.Minecraft;

public static class MinecraftIdentifier
{
    public static string DefaultNamespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.IndexOf(':');
        return separator < 0 ? "minecraft:" + value : separator == 0 ? "minecraft" + value : value;
    }
}
