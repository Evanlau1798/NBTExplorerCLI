using Next.Domain;
using System.Text.Json;
using static Next.Formats.ContractJson;

namespace Next.Formats;

public static class ApplyReceiptJson
{
    public const string Family = "nbtcli.next.apply-receipt";
    public const int Version = 1;
    private sealed record ResultDto(string? OperationId, OperationOutcome? Outcome, string? Reason, ImageProof[]? Proofs);
    private sealed record CountDto(OperationOutcome? Outcome, int? Count);
    private sealed record ReceiptDto(Hash256? PlanHash, long? Revision, string[]? RequestedOperationIds, ResultDto?[]? Results,
        bool? DependenciesComplete, int? Requested, CountDto?[]? Counts, bool? Success);
    public static string Write(ApplyReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return ContractJson.Write(Family, new ReceiptDto(receipt.PlanHash, receipt.Revision, receipt.RequestedOperationIds.ToArray(),
            receipt.Results.Select(result => new ResultDto(result.OperationId, result.Outcome, result.Reason, result.Proofs.ToArray())).ToArray(),
            receipt.DependenciesComplete, receipt.Requested, receipt.Counts.OrderBy(item => item.Key).Select(item => new CountDto(item.Key, item.Value)).ToArray(), receipt.Success));
    }
    public static ApplyReceipt Read(string json, PatchPlan plan, PreparedManifest? prepared = null) => ContractJson.Read(json, Family, data =>
    {
        ArgumentNullException.ThrowIfNull(plan);
        var dto = Decode<ReceiptDto>(data);
        if (dto.PlanHash != plan.PlanHash || !Required(dto.RequestedOperationIds).Order(StringComparer.Ordinal)
                .SequenceEqual(plan.Operations.Select(operation => operation.Id), StringComparer.Ordinal))
            throw new JsonException("Receipt belongs to a different plan or operation set");
        var results = Required(dto.Results).Select(item =>
        {
            var result = Required(item);
            return new OperationReceipt(Required(result.OperationId), result.Outcome ?? throw new JsonException("Missing terminal outcome"), result.Reason, Required(result.Proofs));
        }).ToArray();
        var revision = dto.Revision ?? throw new JsonException("Missing receipt revision");
        var complete = dto.DependenciesComplete ?? throw new JsonException("Missing dependency status");
        var receipt = prepared is null ? plan.Receipt(revision, results, complete) : prepared.Receipt(plan, revision, results, complete);
        var counts = Required(dto.Counts).Select(item => Required(item)).ToDictionary(item => item.Outcome ?? throw new JsonException("Missing counted outcome"),
            item => item.Count ?? throw new JsonException("Missing outcome count"));
        if (dto.Requested != receipt.Requested || dto.Success != receipt.Success || counts.Count != 9
            || receipt.Counts.Any(item => !counts.TryGetValue(item.Key, out var count) || count != item.Value))
            throw new JsonException("Receipt counts/success differ from its terminal records");
        return receipt;
    });
}
