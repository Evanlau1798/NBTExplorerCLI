using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Next.Domain;

namespace Next.Formats;

public sealed record LegacyBatchOperation(int Index, string? Id, string? Path, string? Value, int? LineIndex, string? Problem, ImmutableArray<string> Diagnostics);
public sealed record LegacyBatchInput(string? WorldPath, ImmutableArray<LegacyBatchOperation> Operations, bool CreateBackup, string? Problem, ImmutableArray<string> Diagnostics);
public sealed record LegacyBatchMapping(int Index, string OperationId, string? LegacyId, string? LegacyPath, int? LineIndex, SourceAddress? Address, string Decision, string? Reason, ImmutableArray<string> Diagnostics);

public static class LegacyBatchJson
{
    public const int MaximumInputBytes = 16_777_216;
    public static LegacyBatchInput Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumInputBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Legacy batch exceeds input budget");
        try
        {
            using var document = BoundedJson.ReadDecoded(bytes, new(MaxDecodedBytes: MaximumInputBytes, MaxNodes: 500_000, MaxContainerEntries: 100_000, MaxCollectionElements: 100_000));
            var diagnostics = ImmutableArray.CreateBuilder<string>(); var root = Fields(document.RootElement, diagnostics, "WorldPath", "Operations", "Options");
            var world = Text(root, "WorldPath"); var backup = true;
            if (root.GetValueOrDefault("Options") is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } options)
            {
                var values = Fields(options, diagnostics, "CreateBackup", "ValidateBeforeWrite");
                if (values.TryGetValue("CreateBackup", out var create)) backup = Boolean(create);
                if (values.TryGetValue("ValidateBeforeWrite", out var validate)) { _ = Boolean(validate); diagnostics.Add("LegacyFieldUnused:ValidateBeforeWrite"); }
            }
            if (!root.TryGetValue("Operations", out var operations) || operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > 100_000)
                throw new JsonException("Expected a nonempty bounded legacy operation array");
            var result = ImmutableArray.CreateBuilder<LegacyBatchOperation>();
            foreach (var operation in operations.EnumerateArray())
            {
                var notes = ImmutableArray.CreateBuilder<string>();
                try
                {
                    var fields = Fields(operation, notes, "Id", "Path", "Value", "LineIndex"); var id = Text(fields, "Id"); var path = Text(fields, "Path"); var value = Text(fields, "Value"); int? line = null;
                    if (fields.TryGetValue("LineIndex", out var index) && index.ValueKind != JsonValueKind.Null)
                    { if (!index.TryGetInt32(out var number)) throw new JsonException("Invalid legacy LineIndex"); line = number; notes.Add("LegacyFieldUnused:LineIndex"); }
                    result.Add(new(result.Count, id, path, value, line, string.IsNullOrWhiteSpace(path) || value is null ? "MissingLegacyPathOrValue" : null, notes.ToImmutable()));
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException)
                { result.Add(new(result.Count, null, null, null, null, "InvalidLegacyOperation", notes.ToImmutable())); }
            }
            return new(world, result.ToImmutable(), backup, string.IsNullOrWhiteSpace(world) ? "MissingLegacyWorldPath" : null, diagnostics.ToImmutable());
        }
        catch (CodecException error) when (error.Code == CodecError.LimitExceeded)
        { return new(null, [], true, "LegacyInputLimit", []); }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { return new(null, [], true, "InvalidLegacyBatchJson", []); }
    }
    private static Dictionary<string, JsonElement> Fields(JsonElement element, ImmutableArray<string>.Builder diagnostics, params string[] known)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Expected legacy object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var name = NativeJsonStrings.PropertyName(property);
            if (!fields.TryAdd(name, property.Value)) throw new JsonException("Case-insensitive duplicate legacy field");
            if (!known.Contains(name, StringComparer.OrdinalIgnoreCase)) diagnostics.Add("LegacyUnknownField:" + name);
        }
        return fields;
    }
    private static string? Text(Dictionary<string, JsonElement> fields, string name)
        => !fields.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
            : value.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(value) : throw new JsonException("Legacy text field differs from its declared type");
    private static bool Boolean(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new JsonException("Expected legacy boolean preference");
    private sealed record ReportData(Hash256 InputHash, string RawInputBase64, Guid WorldId, string? WorldPath, bool CreateBackup,
        bool MandatoryValidation, bool MandatoryRecoveryBackup, ImmutableArray<string> Diagnostics, string? Problem,
        int Requested, IReadOnlyList<LegacyBatchMapping> Items,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Hash256? PreviewPlanSha256);
    public static string Report(byte[] raw, Guid worldId, LegacyBatchInput input, IReadOnlyList<LegacyBatchMapping> items, string? problem, Hash256? previewPlanSha256 = null)
        => ContractJson.WriteForStorage("nbtcli.next.legacy-batch-report", new ReportData(Hash256.Compute(raw), Convert.ToBase64String(raw),
            worldId, input.WorldPath, input.CreateBackup, true, true, input.Diagnostics, problem, input.Operations.Length, items, previewPlanSha256));
}
