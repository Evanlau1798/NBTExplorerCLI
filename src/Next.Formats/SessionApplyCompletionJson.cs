using Next.Domain;

namespace Next.Formats;

public static class SessionApplyCompletionJson
{
    private const string Family = "nbtcli.next.session-apply-completion";
    private sealed record CompletionDto(Guid OperationId, Guid SessionId, long Revision, string Plan, string Prepared, string Receipt);
    public static string Write(SessionApplyCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return ContractJson.Write(Family, new CompletionDto(completion.OperationId, completion.SessionId, completion.Revision,
            PatchPlanJson.Write(completion.Plan), PreparedManifestJson.Write(completion.Prepared), ApplyReceiptJson.Write(completion.Receipt)));
    }
    public static SessionApplyCompletion Read(string json) => ContractJson.Read(json, Family, data =>
    {
        ContractJson.Fields(data, "operationId", "sessionId", "revision", "plan", "prepared", "receipt");
        var dto = ContractJson.Decode<CompletionDto>(data); var plan = PatchPlanJson.Read(dto.Plan);
        var prepared = PreparedManifestJson.Read(dto.Prepared, plan);
        return new SessionApplyCompletion(dto.OperationId, dto.SessionId, dto.Revision, plan, prepared, ApplyReceiptJson.Read(dto.Receipt, plan, prepared));
    });
}
