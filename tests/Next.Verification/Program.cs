using Next.Cli;
using Next.Infrastructure;
using System.Text.Json;

var cases = new (string Name, Action Test)[]
{
    ("state-roots-are-product-isolated", () =>
    {
        var expectedDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nbtcli-next");
        Require(StatePaths.DataRoot == expectedDataRoot, "資料根必須位於新版專屬目錄");
        Require(StatePaths.TemporaryRoot == Path.Combine(Path.GetTempPath(), "nbtcli-next"),
            "暫存根必須位於新版專屬目錄");
    }),
    ("help-identifies-implemented-commands", () =>
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["--help"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit == 0, "help 必須成功退出");
        Require(json.RootElement.GetProperty("success").GetBoolean(), "help 必須標示成功");
        var commands = json.RootElement.GetProperty("data").GetProperty("commands").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Require(commands.SequenceEqual(["help", "version"]), "help 只登錄目前已實作入口");
    }),
    ("version-identifies-new-product", () =>
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["--version"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit == 0, "version 必須成功退出");
        Require(json.RootElement.GetProperty("data").GetProperty("version").GetString() == "2.0.0-alpha.1",
            "version 必須回新版候選版本");
    }),
    ("unknown-command-is-machine-readable-failure", () =>
    {
        using var output = new StringWriter();
        var exit = CliApplication.Run(["not-a-command"], output);
        using var json = JsonDocument.Parse(output.ToString());
        Require(exit != 0, "未知命令必須失敗");
        Require(!json.RootElement.GetProperty("success").GetBoolean(), "未知命令不能冒稱成功");
        Require(json.RootElement.GetProperty("error").GetProperty("code").GetString() == "UNKNOWN_COMMAND",
            "未知命令必須有穩定 machine code");
    })
};
var results = new List<object>();
var failed = 0;
foreach (var item in cases)
{
    try
    {
        item.Test();
        results.Add(new { name = item.Name, passed = true });
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"{item.Name}: {error.Message}");
        results.Add(new { name = item.Name, passed = false, error = error.Message });
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = cases.Length - failed, failed, cases = results }));
return failed == 0 ? 0 : 1;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
