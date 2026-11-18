using NBTExplorerCLI.Domain;
using System.Text.Json;

namespace NBTExplorerCLI.Formats;

public static class SessionApplyCompletionJson
{
    private const string Family = "nbtcli.session-apply-completion";
    private sealed record CompletionDto(Guid OperationId, Guid SessionId, long Revision, string Plan, string Prepared, string Receipt);
    private static CompletionDto Dto(SessionApplyCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return new(completion.OperationId, completion.SessionId, completion.Revision,
            PatchPlanJson.Write(completion.Plan), PreparedManifestJson.Write(completion.Prepared), ApplyReceiptJson.Write(completion.Receipt));
    }
    public static string Write(SessionApplyCompletion completion) => ContractJson.Write(Family, Dto(completion));
    public static byte[] WriteUtf8(SessionApplyCompletion completion) => ContractJson.WriteUtf8(Family, Dto(completion));
    public static SessionApplyCompletion Read(string json) => ContractJson.Read(json, Family, Decode);
    public static SessionApplyCompletion ReadUtf8(ReadOnlyMemory<byte> bytes) => ContractJson.ReadUtf8(bytes, Family, Decode);
    private static SessionApplyCompletion Decode(JsonElement data)
    {
        ContractJson.Fields(data, "operationId", "sessionId", "revision", "plan", "prepared", "receipt");
        var dto = ContractJson.Decode<CompletionDto>(data); var plan = PatchPlanJson.Read(dto.Plan);
        var prepared = PreparedManifestJson.Read(dto.Prepared, plan);
        return new SessionApplyCompletion(dto.OperationId, dto.SessionId, dto.Revision, plan, prepared, ApplyReceiptJson.Read(dto.Receipt, plan, prepared));
    }
}
