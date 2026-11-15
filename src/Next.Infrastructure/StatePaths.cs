namespace Next.Infrastructure;

public static class StatePaths
{
    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nbtcli-next");

    public static string TemporaryRoot => Path.Combine(Path.GetTempPath(), "nbtcli-next");
}
