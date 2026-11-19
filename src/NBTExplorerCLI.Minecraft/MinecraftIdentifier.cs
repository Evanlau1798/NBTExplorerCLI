namespace NBTExplorerCLI.Minecraft;

public static class MinecraftIdentifier
{
    public static string DefaultNamespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.IndexOf(':');
        return separator < 0 ? "minecraft:" + value : separator == 0 ? "minecraft" + value : value;
    }

    internal static bool ResourceLocation(string text)
    {
        var separator = text.IndexOf(':');
        if (separator >= 0 && !text.AsSpan(0, separator).ToString().All(ResourceCharacter)) return false;
        var resource = text[(separator + 1)..];
        return resource.Length != 0 && resource.All(ch => ch == '/' || ResourceCharacter(ch));
    }

    private static bool ResourceCharacter(char ch) => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch is '_' or '-' or '.';
}
