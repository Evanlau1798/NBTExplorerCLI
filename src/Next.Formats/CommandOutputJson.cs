using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Next.Domain;

namespace Next.Formats;

public sealed class JsonOutputLimitException(string message = "Contract output exceeds byte budget", Exception? inner = null) : JsonException(message, inner);

public static class CommandOutputJson
{
    private const int MaximumDiagnosticCodeUnits = 4096;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(ContractJson.Options); options.Converters.Insert(0, new NativeElementConverter()); return options;
    }
    private sealed class NativeElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        { using var document = JsonDocument.ParseValue(ref reader); return document.RootElement.Clone(); }
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
    }
    private sealed record Error(string Code, string Message, string? Details);
    private sealed record Envelope(string SchemaFamily, int SchemaVersion, bool Success, string Command, string Outcome, object? Data, Error? Error);
    public static string Write(string command, object? data, string outcome = "Success", string? code = null, string? message = null, string? details = null)
        => ContractJson.SerializeBounded(new Envelope("nbtcli.next.cli", 1, code is null, command, outcome, data, code is null ? null : new(code, message ?? code,
            details is { Length: > MaximumDiagnosticCodeUnits } ? details[..(MaximumDiagnosticCodeUnits - 1)] + "…" : details)), Options);
    public static object RejectedOperations(IReadOnlyList<string> operationIds, string phase) => new
    {
        validation = new { phase, requested = operationIds.Count,
            results = operationIds.Select(operationId => new { operationId, outcome = OperationOutcome.Skipped, reason = phase }).ToArray(),
            counts = Enum.GetValues<OperationOutcome>().Select(outcome => new { outcome, count = outcome == OperationOutcome.Skipped ? operationIds.Count : 0 }).ToArray() }
    };
    public static void ValidateRejectionBudget(IReadOnlyList<string> operationIds)
        => ValidateEnvelopeHeadroom(Write("restore", RejectedOperations(operationIds, "PreparationRejected"), "Unsupported", "PREPARATION_FAILED",
            new string('\uffff', 256), new string('\uffff', MaximumDiagnosticCodeUnits)));
    internal static void ValidateEnvelopeHeadroom(string json)
    {
        // Reserve space for CLI state fields and the bounded export diagnostic around an intact receipt.
        if (Encoding.UTF8.GetByteCount(json) > 67_108_864 - 65_536) throw new JsonOutputLimitException("Operation results leave insufficient command envelope space");
    }
}
