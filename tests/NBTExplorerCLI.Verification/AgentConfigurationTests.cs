using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class AgentConfigurationTests
{
    private sealed class ReadOnlyStore(byte[] bytes) : IAgentConfigurationStore
    {
        public int Writes { get; private set; }
        public byte[]? Read() => bytes.ToArray();
        public void Write(ReadOnlyMemory<byte> value, Hash256? expected) { Writes++; throw new InvalidOperationException("This control must not publish state"); }
    }
    public static void BoundedHistoryRetainsExactReplayAndRejectsCorruption()
    {
        var world = Guid.NewGuid(); var configuration = new AgentConfiguration("agent", 500, 30, false, "en_us", "zh_tw", 6);
        var entries = ImmutableArray.CreateBuilder<AgentConfigurationSubmission>();
        for (var index = 0; index < AgentConfigurationJson.MaximumSubmissions; index++)
        {
            var request = new AgentQuestionnaire(Guid.NewGuid(), world, index - 1, configuration, []);
            var raw = Encoding.UTF8.GetBytes(AgentConfigurationJson.WriteQuestionnaire(request));
            entries.Add(new(new(request.OperationId, Hash256.Compute(raw), index - 1, index), Convert.ToBase64String(raw)));
        }
        var encoded = AgentConfigurationJson.WriteState(new(world, entries.Count - 1, configuration, entries.ToImmutable()));
        var store = new ReadOnlyStore(Encoding.UTF8.GetBytes(encoded)); var service = new AgentConfigurationService(store, world);
        Require(service.Read()!.Submissions.Length == 1024, "the published history boundary is itself readable");
        var additional = Encoding.UTF8.GetBytes(AgentConfigurationJson.WriteQuestionnaire(new(Guid.NewGuid(), world, 1023, configuration, [])));
        var limited = false; try { service.Submit(additional); } catch (JsonOutputLimitException) { limited = true; }
        var replay = service.Submit(Convert.FromBase64String(entries[0].InputBase64));
        Require(limited && replay.Revision == 0 && store.Writes == 0, "history capacity refuses a new submission as ResourceLimit while exact historical replay remains read-only");
        foreach (var member in new[] { "configuration", "inputHash" })
        {
            var node = JsonNode.Parse(encoded)!;
            if (member == "configuration") node["data"]!["configuration"]!["agentName"] = "forged";
            else node["data"]!["submissions"]![0]!["receipt"]!["inputHash"] = Hash256.Compute([99]).Hex;
            var rejected = false; try { AgentConfigurationJson.ReadState(node.ToJsonString()); } catch (JsonException) { rejected = true; }
            Require(rejected, "stored configuration and raw submission identity must agree: " + member);
        }
    }
}
