using System.Text;
using Next.Domain;
using Next.Formats;

namespace Next.Application;

public interface IAgentConfigurationStore
{
    byte[]? Read();
    void Write(ReadOnlyMemory<byte> bytes, Hash256? expected);
}

public sealed class AgentConfigurationService(IAgentConfigurationStore store, Guid worldId)
{
    private readonly IAgentConfigurationStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Guid worldId = worldId != Guid.Empty ? worldId : throw new ArgumentException("World scope is required", nameof(worldId));
    public AgentConfigurationState? Read()
    {
        var bytes = store.Read(); return bytes is null ? null : Decode(bytes);
    }
    private AgentConfigurationState Decode(byte[] bytes)
    {
        var state = AgentConfigurationJson.ReadState(new UTF8Encoding(false, true).GetString(bytes));
        if (state.WorldId != worldId) throw new ArtifactConflictException("Agent configuration belongs to a different world root"); return state;
    }
    public AgentConfigurationReceipt Submit(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > AgentConfigurationJson.MaximumRequestBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Agent input exceeds its byte budget");
        var request = AgentConfigurationJson.ReadQuestionnaire(new UTF8Encoding(false, true).GetString(bytes.Span)); request.Configuration.Validate();
        if (request.WorldId != worldId) throw new ArtifactConflictException("Questionnaire belongs to a different world root");
        var input = Hash256.Compute(bytes.Span); var previous = store.Read(); var state = previous is null ? null : Decode(previous);
        AgentConfigurationReceipt? Replay(AgentConfigurationState? current)
        {
            var retained = current?.Submissions.SingleOrDefault(item => item.Receipt.OperationId == request.OperationId);
            if (retained is not null && retained.Receipt.InputHash != input) throw new ArtifactConflictException("Agent operation identity was reused with different input");
            return retained?.Receipt;
        }
        if (Replay(state) is { } replay) return replay;
        if (request.Revision != (state?.Revision ?? -1)) throw new ArtifactConflictException("Agent configuration revision changed");
        if (state?.Submissions.Length >= AgentConfigurationJson.MaximumSubmissions) throw new JsonOutputLimitException("Agent submission history budget exceeded");
        var receipt = new AgentConfigurationReceipt(request.OperationId, input, request.Revision, checked(request.Revision + 1));
        var submissions = (state?.Submissions ?? []).Add(new(receipt, Convert.ToBase64String(bytes.Span)));
        var changed = new AgentConfigurationState(worldId, receipt.Revision, request.Configuration, submissions);
        var encoded = Encoding.UTF8.GetBytes(AgentConfigurationJson.WriteState(changed));
        try { store.Write(encoded, previous is null ? null : Hash256.Compute(previous)); }
        catch (ArtifactConflictException) { return Replay(Read()) ?? throw new ArtifactConflictException("Agent configuration changed during publication"); }
        return receipt;
    }
}
