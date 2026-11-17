using System.Text.Json;
using System.Text.Json.Serialization;

namespace Next.Formats;

public sealed class JsonOutputLimitException() : JsonException("Contract output exceeds byte budget");

public static class CommandOutputJson
{
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
        => ContractJson.SerializeBounded(new Envelope("nbtcli.next.cli", 1, code is null, command, outcome, data, code is null ? null : new(code, message ?? code, details)), Options);
}
