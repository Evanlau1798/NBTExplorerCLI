using System.Collections.Immutable;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;

namespace NBTExplorerCLI.Cli;

internal static class AgentCommands
{
    internal static readonly string[] Subcommands = ["setup", "submit", "status", "help"];
    internal static string[] Allowed(string action) => action switch
    {
        "help" => ["help"],
        "setup" => ["state-root", "output", "help", "source-language", "target-language", "operation-id"],
        "submit" => ["state-root", "output", "help", "request"],
        "status" => ["state-root", "output", "help"],
        _ => throw new CliFailure("UNKNOWN_COMMAND", "Unknown agent subcommand")
    };
    internal static AgentConfiguration? Configuration(WriteContext context)
    {
        var root = Path.Combine(context.State, "agent");
        return Path.Exists(root) ? new AgentConfigurationService(new FileSystemAgentConfigurationStore(root), context.Inventory.WorldId).Read()?.Configuration : null;
    }
    internal static CliResult Execute(CliArguments request)
    {
        byte[]? submitted = null; AgentQuestionnaire? questionnaire = null;
        if (request.Subcommand == "submit")
        {
            submitted = CliInput.ReadBytes(request.Get("request") ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --request"), AgentConfigurationJson.MaximumRequestBytes);
            questionnaire = AgentConfigurationJson.ReadQuestionnaire(CliInput.Decode(submitted));
        }
        var operationId = Guid.NewGuid();
        if (request.Get("operation-id") is { } operation && (!Guid.TryParse(operation, out operationId) || operationId == Guid.Empty)) throw new CliFailure("INVALID_ARGUMENTS", "Use a nonempty operation GUID");
        var context = new WriteContext(request.Input!, request);
        var service = new AgentConfigurationService(new FileSystemAgentConfigurationStore(Path.Combine(context.State, "agent")), context.Inventory.WorldId);
        if (request.Subcommand == "setup")
        {
            var current = service.Read(); var configuration = current?.Configuration ?? new("", 500, 30, true,
                TranslationLanguages.Automatic, TranslationLanguages.DisplayLanguage() ?? "", 6);
            configuration = configuration with { SourceLanguage = request.Get("source-language") ?? configuration.SourceLanguage, TargetLanguage = request.Get("target-language") ?? configuration.TargetLanguage };
            var template = new AgentQuestionnaire(operationId, context.Inventory.WorldId, current?.Revision ?? -1, configuration, Instructions);
            var json = AgentConfigurationJson.WriteQuestionnaire(template);
            return new(new { questionnaire = Json(json), suggestedCommand = "agent submit <world> --request <answered-file>" }, Export: json);
        }
        if (request.Subcommand == "submit")
        {
            try
            {
                var receipt = service.Submit(submitted!); var current = service.Read()!;
                return new(new { receipt, revision = current.Revision, configuration = current.Configuration, suggestedCommand = "translate start <world> --session-id <new-guid>" });
            }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException or InvalidDataException) && error is not ArtifactConflictException)
            { throw new CliFailure("AGENT_STATE_IO", error.Message, new { operationId = questionnaire!.OperationId, repair = "retry-same-questionnaire" }); }
        }
        var state = service.Read() ?? throw new CliFailure("NOT_FOUND", "No confirmed agent configuration; run agent setup", new { repair = "agent-setup" });
        return new(new { revision = state.Revision, configuration = state.Configuration, submissions = state.Submissions.Length });
    }
    private static readonly ImmutableArray<string> Instructions =
        ["Fill in the agent name, source/target languages and bounded capability preferences; retain operationId, worldId and revision.",
         "Return this versioned questionnaire to agent submit. Keep the original files; exact resends are idempotent.",
         "Use complete decoded slot reply sets. Preserve formatting/placeholders; report uncertainty with notes and use the glossary consistently.",
         "Addresses, commands, AST, events, styles and UUIDs stay under local validation. Preferences grant no write or web permission.",
         "MaxLinesPerPage, CanSearchWeb and LowConfidenceThreshold describe client reading/research/review preferences. This product does not call a model or browse for you.",
         "Start a new translation session after submission. Pass language options to override automatic defaults for that session."];
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 }); return document.RootElement.Clone(); }
}
