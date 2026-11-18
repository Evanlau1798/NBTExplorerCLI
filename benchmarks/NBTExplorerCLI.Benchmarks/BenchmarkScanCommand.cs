using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;

namespace NBTExplorerCLI.Benchmarks;

public static class BenchmarkScanCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Count is not (5 or 6) || args[0] != "measure-scan" || args[3] is not ("full" or "reuse")
                || !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var workers) || workers is < 1 or > 8
                || args.Count == 6 && args[5] is not ("all" or "language.value"))
                throw new ArgumentException("Use measure-scan WORLD CACHE full|reuse WORKERS [all|language.value]");
            var world = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1])); var cache = Path.GetFullPath(args[2]);
            var prefix = Path.EndsInDirectorySeparator(world) ? world : world + Path.DirectorySeparatorChar;
            if (cache.Equals(world, StringComparison.OrdinalIgnoreCase) || cache.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Measurement cache must be outside the source world");
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(world)) throw new DirectoryNotFoundException("Measurement world not found");
            var probe = new WorldInventory(world, new("473a419f-48ab-43d4-bc1a-d742415d27ae"));
            var inventory = new WorldInventory(world, new Guid(Convert.FromHexString(probe.AccessBinding.Hex).AsSpan(0, 16)));
            var scope = args.Count == 6 && args[5] != "all" ? new SnapshotScope([], [], [args[5]], []) : SnapshotScope.All;
            var sample = await ScanMeasurement.RunAsync(inventory, new FileSystemScanCacheStore(cache), new(scope), new(workers), args[3] == "full", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine(JsonSerializer.Serialize(new
            {
                schemaFamily = "nbtcli.benchmark", schemaVersion = 1, success = sample.CoverageComplete,
                configuration = new { world, cache, mode = args[3], workers, scope = args.Count == 6 ? args[5] : "all",
                    runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), referenceCoverage = "not-assessed" },
                data = sample, error = sample.CoverageComplete ? null : new { code = "INCOMPLETE_SOURCES", message = "Measured workload retains incomplete or failed sources" }
            }, Json));
            return sample.CoverageComplete ? 0 : 4;
        }
        catch (Exception error)
        {
            var exit = error is ArgumentException ? 2 : error is OperationCanceledException ? 130 : error is NotSupportedException ? 4 : 5;
            output.WriteLine(JsonSerializer.Serialize(new { schemaFamily = "nbtcli.benchmark", schemaVersion = 1, success = false,
                error = new { code = exit == 2 ? "INVALID_MEASUREMENT_REQUEST" : exit == 130 ? "CANCELLED" : "MEASUREMENT_FAILED",
                    message = error.Message.Length > 4096 ? error.Message[..4096] : error.Message } }, Json));
            return exit;
        }
    }
}
