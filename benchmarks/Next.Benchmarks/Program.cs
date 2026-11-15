using System.Text.Json;
using Next.Benchmarks;

if (args.Length == 3 && args[0] == "generate-corpus")
{
    try
    {
        var summary = CorpusBuilder.Generate(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, data = summary }));
        return 0;
    }
    catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(error.Message);
        Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = new { code = "CORPUS_GENERATION_FAILED", message = error.Message } }));
        return 2;
    }
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    schemaFamily = "nbtcli.next.benchmark",
    schemaVersion = 1,
    success = false,
    error = new { code = "BENCHMARK_NOT_READY", message = "G06 串行 control 尚未建立。" }
}));
return 2;
