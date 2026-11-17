using System.Text.Json;
using Next.Benchmarks;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length > 0 && args[0] == "measure-scan")
{
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, signal) =>
    {
        signal.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += onCancel;
    try { return await BenchmarkScanCommand.RunAsync(args, Console.Out, cancellation.Token); }
    finally { Console.CancelKeyPress -= onCancel; }
}

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
    error = new { code = "BENCHMARK_NOT_READY", message = "請使用 generate-corpus 或 measure-scan；完整 G07 工作流量測尚未建立。" }
}));
return 2;
