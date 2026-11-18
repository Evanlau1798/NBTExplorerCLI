using NBTExplorerCLI.Domain;
using System.Text.Json;

namespace NBTExplorerCLI.Formats;

public static class TypedNbtJson
{
    public const string Family = DataSchemas.TypedNbt;
    public const int Version = 1;
    private sealed record DocumentDto(string? Name, NbtValue? Root);
    public static string Write(NbtDocument document, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Check(document, limits);
        var json = ContractJson.Write(Family, new DocumentDto(document.Name, document.Root));
        _ = ContractJson.Read(json, Family, _ => true);
        return json;
    }
    public static NbtDocument Read(string json, DecodeLimits? limits = null) => ContractJson.Read(json, Family, data =>
    {
        var dto = ContractJson.Decode<DocumentDto>(data);
        var document = new NbtDocument(dto.Name ?? throw new JsonException("Missing root name"),
            dto.Root ?? throw new JsonException("Missing typed root"));
        Check(document, limits);
        return document;
    });
    private static void Check(NbtDocument document, DecodeLimits? limits)
    {
        _ = BinaryNbt.MeasureDocument(document, limits);
    }
}
