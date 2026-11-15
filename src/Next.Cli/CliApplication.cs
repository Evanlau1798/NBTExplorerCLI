using System.Text.Json;
using System.Reflection;

namespace Next.Cli;

public static class CliApplication
{
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        var command = args.Count == 0 ? "help" : args[0];
        object? data = null;
        if (args.Count <= 1)
        {
            if (command is "help" or "--help" or "-h")
            {
                command = "help";
                data = new { usage = "nbtcli-next <command> [options]", commands = new[] { "help", "version" } };
            }
            else if (command is "version" or "--version" or "-v")
            {
                command = "version";
                var version = typeof(CliApplication).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
                data = new { product = "nbtcli-next", version };
            }
        }
        var success = data is not null;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schemaFamily = "nbtcli.next.cli",
            schemaVersion = 1,
            success,
            command,
            outcome = success ? "Success" : "Failed",
            data,
            error = success ? null : new { code = "UNKNOWN_COMMAND", message = "未知命令或不支援的參數。" }
        }));
        return success ? 0 : 2;
    }
}
