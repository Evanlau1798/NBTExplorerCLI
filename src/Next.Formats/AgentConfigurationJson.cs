using System.Text;
using System.Text.Json;
using Next.Domain;

namespace Next.Formats;

public static class AgentConfigurationJson
{
    public const string QuestionnaireFamily = "nbtcli.next.agent-questionnaire";
    public const string StateFamily = "nbtcli.next.agent-configuration";
    public const int MaximumRequestBytes = 16_384;
    public const int MaximumSubmissions = 1024;
    private static void RequestSize(string json)
    {
        if (json.Length > MaximumRequestBytes || Encoding.UTF8.GetByteCount(json) > MaximumRequestBytes)
            throw new CodecException(CodecError.LimitExceeded, 0, "Agent questionnaire exceeds its byte budget");
    }
    public static string WriteQuestionnaire(AgentQuestionnaire value)
    {
        var json = ContractJson.WriteBounded(QuestionnaireFamily, value); RequestSize(json); _ = ReadQuestionnaire(json); return json;
    }
    public static AgentQuestionnaire ReadQuestionnaire(string json)
    {
        RequestSize(json);
        return ContractJson.Read(json, QuestionnaireFamily, data =>
        {
            ContractJson.Fields(data, "operationId", "worldId", "revision", "configuration", "instructions");
            var value = ContractJson.Decode<AgentQuestionnaire>(data);
            if (value.OperationId == Guid.Empty || value.WorldId == Guid.Empty || value.Revision < -1 || value.Configuration is null || value.Instructions.IsDefault
                || value.Instructions.Length > 32 || value.Instructions.Any(text => text is null || text.Length > 4096)) throw new JsonException("Invalid questionnaire binding or instructions");
            return value;
        });
    }
    public static string WriteState(AgentConfigurationState state)
    {
        Validate(state); return ContractJson.WriteForStorage(StateFamily, state);
    }
    public static AgentConfigurationState ReadState(string json) => ContractJson.Read(json, StateFamily, data =>
    {
        ContractJson.Fields(data, "worldId", "revision", "configuration", "submissions");
        var state = ContractJson.Decode<AgentConfigurationState>(data); Validate(state);
        if (ContractJson.WriteBounded(StateFamily, state) != json) throw new JsonException("Agent configuration state must be canonical");
        return state;
    });
    private static void Validate(AgentConfigurationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.WorldId == Guid.Empty || state.Configuration is null || state.Submissions.IsDefaultOrEmpty || state.Submissions.Length > MaximumSubmissions
            || state.Revision != state.Submissions.Length - 1) throw new JsonException("Invalid agent configuration history");
        state.Configuration.Validate(); var ids = new HashSet<Guid>(); AgentConfiguration? latest = null;
        for (var index = 0; index < state.Submissions.Length; index++)
        {
            var item = state.Submissions[index];
            if (item is null || item.Receipt is not { } receipt || !ids.Add(receipt.OperationId) || receipt.InputHash is null || item.InputBase64 is null
                || item.InputBase64.Length > 21_848 || receipt.FromRevision != index - 1 || receipt.Revision != index) throw new JsonException("Invalid agent submission history");
            var raw = Convert.FromBase64String(item.InputBase64);
            if (raw.Length > MaximumRequestBytes || Convert.ToBase64String(raw) != item.InputBase64 || Hash256.Compute(raw) != receipt.InputHash) throw new JsonException("Retained agent input differs from its identity");
            var request = ReadQuestionnaire(new UTF8Encoding(false, true).GetString(raw)); request.Configuration.Validate();
            if (request.OperationId != receipt.OperationId || request.WorldId != state.WorldId || request.Revision != receipt.FromRevision) throw new JsonException("Agent history binding differs");
            latest = request.Configuration;
        }
        if (latest != state.Configuration) throw new JsonException("Current agent configuration differs from accepted history");
    }
}
