using System.Text.Json;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class OperationSetJson
{
    public const string Family = "nbtcli.operations";
    private sealed record Body(PatchOperation[] Operations);
    public static IReadOnlyList<PatchOperation> Read(string json) => ContractJson.Read(json, Family, data =>
    {
        var operations = ContractJson.Decode<Body>(data).Operations;
        if (operations is null || operations.Length is < 1 or > 100_000 || operations.Any(operation => operation is null)
            || operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count() != operations.Length) throw new JsonException("Invalid bounded operation set");
        return operations;
    });
}
