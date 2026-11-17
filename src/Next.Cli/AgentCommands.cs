using System.Collections.Immutable;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

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
            var current = service.Read(); var configuration = current?.Configuration ?? new("", 500, 30, true, "", "zh_tw", 6);
            configuration = configuration with { SourceLanguage = request.Get("source-language") ?? configuration.SourceLanguage, TargetLanguage = request.Get("target-language") ?? configuration.TargetLanguage };
            var template = new AgentQuestionnaire(operationId, context.Inventory.WorldId, current?.Revision ?? -1, configuration, Instructions(request.Locale));
            var json = AgentConfigurationJson.WriteQuestionnaire(template);
            return new(new { questionnaire = Json(json), nextCommand = "agent submit <world> --request <answered-file>" }, Export: json);
        }
        if (request.Subcommand == "submit")
        {
            try
            {
                var receipt = service.Submit(submitted!); var current = service.Read()!;
                return new(new { receipt, revision = current.Revision, configuration = current.Configuration, nextCommand = "translate start <world> --session-id <new-guid>" });
            }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException or InvalidDataException) && error is not ArtifactConflictException)
            { throw new CliFailure("AGENT_STATE_IO", error.Message, new { operationId = questionnaire!.OperationId, repair = "retry-same-questionnaire" }); }
        }
        var state = service.Read() ?? throw new CliFailure("NOT_FOUND", "No confirmed agent configuration; run agent setup", new { repair = "agent-setup" });
        return new(new { revision = state.Revision, configuration = state.Configuration, submissions = state.Submissions.Length });
    }
    private static ImmutableArray<string> Instructions(string locale) => locale == "en-US" ?
        ["Fill in the agent name, source/target languages and bounded capability preferences; retain operationId, worldId and revision.",
         "Return this versioned questionnaire to agent submit. Keep the original files; exact resends are idempotent.",
         "Use complete decoded slot reply sets. Preserve formatting/placeholders; report uncertainty with notes and use the glossary consistently.",
         "Addresses, commands, AST, events, styles and UUIDs stay under local validation. Preferences grant no write or web permission.",
         "MaxLinesPerPage, CanSearchWeb and LowConfidenceThreshold describe client reading/research/review preferences. This product does not call a model or browse for you.",
         "After submission explicitly start a new translation session. Existing sessions and world contents are not changed."] :
        ["填寫 agent 名稱、來源／目標語言及有界能力偏好；保留 operationId、worldId 與 revision。",
         "將此版本化問卷交給 agent submit。原檔會保留，相同原始輸入可冪等重送。",
         "只回覆完整 decoded slot 集合，保留格式／placeholder，低信心譯文用 note 註明並一致使用術語。",
         "地址、命令、AST、事件、樣式與 UUID 仍由本機驗證；偏好不能授予世界寫入或網路權限。",
         "MaxLinesPerPage、CanSearchWeb、LowConfidenceThreshold 描述客戶端讀取／查證／審閱偏好；本程式不代呼叫模型或瀏覽網站。",
         "提交後明確建立新的翻譯 session；不會改寫既有 session 或世界內容。"];
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 }); return document.RootElement.Clone(); }
}
