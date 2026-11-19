using NBTExplorerCLI.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBTExplorerCLI.Formats;

public static class TextSlotJson
{
    public const string Family = "nbtcli.text-slot";
    public const int Version = 1;
    private sealed record SlotDto(SourceAddress? Address, StableIdentity? SlotId, string? SourceText, Hash256? SourceTextHash,
        string? Context, string? SourceLocale, string? TargetLocale, bool? AllowedEmpty, string[]? Placeholders,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ProtectVisibleText = false);
    public static string Write(TextSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return ContractJson.Write(Family, new SlotDto(slot.Address, slot.Id, slot.SourceText, slot.SourceTextHash,
            slot.Context, slot.SourceLocale, slot.TargetLocale, slot.AllowedEmpty, slot.Placeholders.Tokens.Order(StringComparer.Ordinal).ToArray(), slot.Placeholders.ProtectVisibleText));
    }
    public static TextSlot Read(string json) => ContractJson.Read(json, Family, data =>
    {
        var fields = new[] { "address", "slotId", "sourceText", "sourceTextHash", "context", "sourceLocale", "targetLocale", "allowedEmpty", "placeholders" };
        ContractJson.Fields(data, data.TryGetProperty("protectVisibleText", out _) ? [.. fields, "protectVisibleText"] : fields);
        var dto = ContractJson.Decode<SlotDto>(data);
        var slot = new TextSlot(dto.Address ?? throw new JsonException("Missing address"), dto.SourceText ?? throw new JsonException("Missing source text"),
            dto.Context ?? throw new JsonException("Missing context"), dto.SourceLocale!, dto.TargetLocale!,
            dto.AllowedEmpty ?? throw new JsonException("Missing empty policy"), new(dto.Placeholders ?? throw new JsonException("Missing placeholders"), dto.ProtectVisibleText));
        return slot.Id == dto.SlotId && slot.SourceTextHash == dto.SourceTextHash ? slot : throw new JsonException("Slot identity/revision claim differs from its data");
    });
}
