using System.Collections.Immutable;

namespace NBTExplorerCLI.Domain;

public sealed record AgentConfiguration(string AgentName, int MaxLinesPerPage, int PreferredItemsPerPage, bool CanSearchWeb,
    string SourceLanguage, string TargetLanguage, int LowConfidenceThreshold)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentName) || AgentName.Length > 256 || string.IsNullOrWhiteSpace(SourceLanguage) || SourceLanguage.Length > 128
            || string.IsNullOrWhiteSpace(TargetLanguage) || TargetLanguage.Length > 128 || MaxLinesPerPage is < 1 or > 500
            || PreferredItemsPerPage is < 1 or > 100_000 || LowConfidenceThreshold is < 0 or > 10)
            throw new ArgumentException("Invalid agent name, languages or bounded capability preferences");
    }
}
public sealed record AgentQuestionnaire(Guid OperationId, Guid WorldId, long Revision, AgentConfiguration Configuration, ImmutableArray<string> Instructions);
public sealed record AgentConfigurationReceipt(Guid OperationId, Hash256 InputHash, long FromRevision, long Revision);
public sealed record AgentConfigurationSubmission(AgentConfigurationReceipt Receipt, string InputBase64);
public sealed record AgentConfigurationState(Guid WorldId, long Revision, AgentConfiguration Configuration, ImmutableArray<AgentConfigurationSubmission> Submissions);
