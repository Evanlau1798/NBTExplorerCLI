using System.Text.Json;

Console.WriteLine(JsonSerializer.Serialize(new
{
    schemaFamily = "nbtcli.next.benchmark",
    schemaVersion = 1,
    success = false,
    error = new { code = "BENCHMARK_NOT_READY", message = "G06 串行 control 尚未建立。" }
}));
return 2;
