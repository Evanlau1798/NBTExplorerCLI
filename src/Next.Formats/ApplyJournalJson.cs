using Next.Domain;
using System.Text.Json;

namespace Next.Formats;

public static class ApplyJournalJson
{
    private sealed record JournalDto(JournalBinding? Binding, Hash256? HeaderHash, long? Sequence, Hash256? PreviousHash,
        ApplyDirection? Direction, ApplyJournalPhase? Phase, int? CompletedCount, Hash256? ReceiptHash);
    public static string Write(ApplyJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        return ContractJson.WriteBounded(ApplyJournal.SchemaFamily, new JournalDto(journal.Binding, journal.HeaderHash,
            journal.Sequence, journal.PreviousHash, journal.Direction, journal.Phase, journal.CompletedCount, journal.ReceiptHash));
    }
    public static ApplyJournal Read(string json) => ContractJson.Read<ApplyJournal>(json, ApplyJournal.SchemaFamily, data =>
    {
        ContractJson.Fields(data, "binding", "headerHash", "sequence", "previousHash", "direction", "phase", "completedCount", "receiptHash");
        var dto = ContractJson.Decode<JournalDto>(data);
        return new(ContractJson.Required(dto.Binding), ContractJson.Required(dto.HeaderHash), dto.Sequence ?? throw new JsonException("Missing journal sequence"), dto.PreviousHash,
            dto.Direction ?? throw new JsonException("Missing journal direction"), dto.Phase ?? throw new JsonException("Missing journal phase"),
            dto.CompletedCount ?? throw new JsonException("Missing journal progress"), dto.ReceiptHash);
    });
}

public static class ApplyJournalHeaderJson
{
    private sealed record HeaderDto(JsonElement Plan, JsonElement Prepared, Hash256? BackupManifestHash, Hash256? AccessBinding);
    public static string Write(ApplyJournalHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        using var plan = JsonDocument.Parse(PatchPlanJson.Write(header.Plan), new JsonDocumentOptions { MaxDepth = 1024 });
        using var prepared = JsonDocument.Parse(PreparedManifestJson.Write(header.Prepared), new JsonDocumentOptions { MaxDepth = 1024 });
        return ContractJson.WriteForStorage(ApplyJournalHeader.SchemaFamily, new HeaderDto(plan.RootElement, prepared.RootElement, header.BackupManifestHash, header.AccessBinding));
    }
    public static ApplyJournalHeader Read(string json) => ContractJson.Read<ApplyJournalHeader>(json, ApplyJournalHeader.SchemaFamily, data =>
    {
        ContractJson.Fields(data, "plan", "prepared", "backupManifestHash", "accessBinding"); var dto = ContractJson.Decode<HeaderDto>(data);
        if (dto.Plan.ValueKind != JsonValueKind.Object || dto.Prepared.ValueKind != JsonValueKind.Object) throw new JsonException("Missing journal plan or prepared manifest");
        var plan = PatchPlanJson.Read(dto.Plan.GetRawText()); var prepared = PreparedManifestJson.Read(dto.Prepared.GetRawText(), plan);
        return new(plan, prepared, ContractJson.Required(dto.BackupManifestHash), ContractJson.Required(dto.AccessBinding));
    });
}
