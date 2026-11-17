using Next.Cli;
using Next.Infrastructure;
using System.Text.Json;

namespace Next.Verification;

internal static class BootstrapTests
{
    public static void StateRoots()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nbtcli-next");
        Require(StatePaths.DataRoot == expected, "資料根必須位於新版專屬目錄");
        Require(StatePaths.TemporaryRoot == Path.Combine(Path.GetTempPath(), "nbtcli-next"), "暫存根必須位於新版專屬目錄");
    }

    public static void Help()
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["--help"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit == 0, "help 必須成功退出");
        Require(json.RootElement.GetProperty("success").GetBoolean(), "help 必須標示成功");
        var commands = json.RootElement.GetProperty("data").GetProperty("commands").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Require(commands.SequenceEqual(["help", "version", "get", "list", "search", "scan", "extract", "validate", "capabilities"]), "help 只登錄目前已有黑箱驗證的實作入口");
    }

    public static void Version()
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["--version"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit == 0, "version 必須成功退出");
        Require(json.RootElement.GetProperty("data").GetProperty("version").GetString() == "2.0.0-alpha.1",
            "version 必須回新版候選版本");
    }

    public static void UnknownCommand()
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["not-a-command"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit != 0, "未知命令必須失敗");
        Require(!json.RootElement.GetProperty("success").GetBoolean(), "未知命令不能冒稱成功");
        Require(json.RootElement.GetProperty("error").GetProperty("code").GetString() == "UNKNOWN_COMMAND",
            "未知命令必須有穩定 machine code");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
