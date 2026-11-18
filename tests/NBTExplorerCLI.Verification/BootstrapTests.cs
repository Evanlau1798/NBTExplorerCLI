using NBTExplorerCLI.Cli;
using NBTExplorerCLI.Infrastructure;
using System.Text.Json;

namespace NBTExplorerCLI.Verification;

internal static class BootstrapTests
{
    public static void StateRoots()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nbtcli");
        Require(StatePaths.DataRoot == expected, "資料根必須位於產品專屬目錄");
        Require(StatePaths.TemporaryRoot == Path.Combine(Path.GetTempPath(), "nbtcli"), "暫存根必須位於產品專屬目錄");
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
        Require(commands.SequenceEqual(["help", "version", "get", "list", "search", "scan", "extract", "validate", "capabilities", "set", "batch", "plan", "apply", "recover", "status", "backup", "restore", "convert", "translate", "agent"]), "help 只登錄目前已有黑箱驗證的實作入口");
    }

    public static void Version()
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["--version"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit == 0, "version 必須成功退出");
        var envelope = json.RootElement; var data = envelope.GetProperty("data");
        Require(envelope.GetProperty("schemaFamily").GetString() == "nbtcli.cli"
            && data.GetProperty("product").GetString() == "nbtcli" && data.GetProperty("version").GetString() == "1.0.0"
            && typeof(CliApplication).Assembly.GetName().Name == "nbtcli", "版本回應、CLI schema 與組件必須使用首版識別");
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
