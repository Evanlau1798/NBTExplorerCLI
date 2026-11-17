using System.Text.Json;
using System.Reflection;
using Next.Domain;
using Next.Application;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

public static class CliApplication
{
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        var command = args.Count == 0 ? "help" : args[0]; var locale = "zh-TW";
        try
        {
            var rest = CliArguments.ExtractLocale(args, out locale, out command);
            var request = CliArguments.Parse(rest, locale); command = request.Command;
            object data;
            if (command == "help" || request.Flag("help") || request.Subcommand == "help")
            {
                var subject = command == "help" ? request.Input : command;
                if (subject is not null) subject = CliArguments.Canonical(subject);
                data = new { usage = "nbtcli-next <command> [options]", commands = CliArguments.Commands,
                    subject, subcommand = request.Subcommand, subcommands = subject switch { "translate" => SessionCommands.Subcommands, "agent" => AgentCommands.Subcommands, _ => Array.Empty<string>() },
                    options = (subject is null ? Array.Empty<string>() : CliArguments.Allowed(subject, request.Subcommand)).Select(option => "--" + option).Append("--ui-locale").ToArray() };
                command = "help";
            }
            else if (command == "version") data = new { product = "nbtcli-next", version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] };
            else if (command is "scan" or "extract" or "validate" or "capabilities" or "set" or "batch" or "plan" or "apply" or "recover" or "status" or "backup" or "restore" or "translate" or "agent" or "import-legacy")
            {
                var result = command == "import-legacy" ? LegacyCommands.Execute(request) : command == "agent" ? AgentCommands.Execute(request) : command == "translate" ? SessionCommands.Execute(request) : command is "scan" or "extract" or "validate" or "capabilities" ? ScanCommands.Execute(request) : WriteCommands.Execute(request);
                var json = CommandOutputJson.Write(command, result.Data, result.Outcome, result.Code, result.Code is null ? null : Message(result.Code, locale));
                if (request.Get("output") is { } file)
                {
                    try { QueryExportFile.WriteNew(file, result.Export ?? (json + Environment.NewLine)); }
                    catch (ArtifactConflictException error)
                    {
                        output.WriteLine(CommandOutputJson.Write(command, result.Data, "Conflict", "CONFLICT", Message("CONFLICT", locale), error.Message)); return 3;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                    {
                        output.WriteLine(CommandOutputJson.Write(command, result.Data, "Failed", "EXPORT_FAILED", Message("EXPORT_FAILED", locale), error.Message)); return 5;
                    }
                }
                output.WriteLine(json); return result.Exit;
            }
            else data = ReadCommands.Execute(request);
            output.WriteLine(CommandOutputJson.Write(command, data)); return 0;
        }
        catch (Exception error)
        {
            var code = Code(error);
            var exit = code switch { "UNKNOWN_COMMAND" or "UNKNOWN_OPTION" or "INVALID_ARGUMENTS" or "CHUNK_REQUIRED" => 2, "CONFLICT" or "AMBIGUOUS_PATH" => 3, "UNSUPPORTED_SOURCE" or "INCOMPLETE_SOURCE" => 4, "RESOURCE_LIMIT" or "PATTERN_TIMEOUT" => 6, "INTERNAL_ERROR" => 1, _ => 5 };
            var outcome = exit == 3 ? "Conflict" : exit == 4 ? "Unsupported" : "Failed";
            output.WriteLine(CommandOutputJson.Write(CliArguments.Canonical(command), (error as CliFailure)?.Result, outcome, code, Message(code, locale), error.Message)); return exit;
        }
    }
    internal static string Code(Exception error) => error switch
    {
        CliFailure failure => failure.Code,
        ArtifactConflictException => "CONFLICT",
        EditRejectedException { Failure: EditFailure.Missing or EditFailure.Ambiguous or EditFailure.Precondition } => "CONFLICT",
        EditRejectedException => "INVALID_ARGUMENTS",
        FileNotFoundException or DirectoryNotFoundException => "FILE_NOT_FOUND",
        KeyNotFoundException => "NOT_FOUND",
        UnauthorizedAccessException => "ACCESS_DENIED",
        ArtifactSizeLimitException or JsonOutputLimitException or SessionPageOversizedException or SessionSlotLimitException or LegacyImportLimitException or CodecException { Code: CodecError.LimitExceeded } => "RESOURCE_LIMIT",
        CodecException { Code: CodecError.UnsupportedProfile } or NotSupportedException => "UNSUPPORTED_SOURCE",
        CodecException or JsonException => "MALFORMED_SOURCE",
        InvalidDataException => "INVALID_DATA",
        IOException => "READ_ERROR",
        ArgumentException => "INVALID_ARGUMENTS",
        _ => "INTERNAL_ERROR"
    };
    private static string Message(string code, string locale)
    {
        var en = locale == "en-US";
        return code switch
        {
            "UNKNOWN_COMMAND" => en ? "Unknown command." : "未知命令。",
            "UNKNOWN_OPTION" => en ? "Unknown option." : "未知選項。",
            "INVALID_ARGUMENTS" or "CHUNK_REQUIRED" => en ? "Invalid or missing arguments." : "參數無效或缺少必要值。",
            "FILE_NOT_FOUND" => en ? "File or directory not found." : "找不到檔案或目錄。",
            "NOT_FOUND" => en ? "The exact target was not found." : "找不到指定的精確目標。",
            "AMBIGUOUS_PATH" or "CONFLICT" => en ? "The target is ambiguous or has changed." : "目標有歧義或來源已變動。",
            "UNSUPPORTED_SOURCE" or "INCOMPLETE_SOURCE" => en ? "The source is unsupported or incomplete." : "來源不支援或不完整。",
            "MALFORMED_SOURCE" => en ? "The source data is malformed." : "來源資料格式損壞。",
            "INVALID_DATA" => en ? "The data failed validation." : "資料未通過驗證。",
            "ACCESS_DENIED" => en ? "Access denied." : "拒絕存取。",
            "RESOURCE_LIMIT" => en ? "The resource limit was exceeded." : "超過資源上限。",
            "PATTERN_TIMEOUT" => en ? "The match pattern timed out." : "比對模式已逾時。",
            "EXPORT_FAILED" => en ? "The export failed; inspect the operation results." : "匯出失敗；請核對操作結果。",
            "RECOVERY_REQUIRED" => en ? "Recovery is required; inspect every operation result." : "需要復原；請核對每項操作結果。",
            "PREPARATION_FAILED" => en ? "Preparation failed before commit; inspect validation results." : "準備階段失敗，尚未提交；請核對驗證結果。",
            "ROLLED_BACK" => en ? "The plan was rolled back." : "計畫已回復至套用前狀態。",
            "READ_ERROR" => en ? "The source could not be read." : "無法讀取來源。",
            "AGENT_STATE_IO" => en ? "Agent state could not be confirmed; retry the exact questionnaire after resolving the I/O error." : "無法確認 agent 設定狀態；排除 I/O 錯誤後請重送同一份問卷。",
            "IMPORT_SESSION_FAILED" => en ? "The report is retained; inspect or resume session projection using the import identity." : "報告已保留；請使用 import 身分檢查或接續 session 建立。",
            "IMPORT_STATE_IO" => en ? "Import persistence is unconfirmed; retain the input, resolve I/O and retry or look up the report identity." : "無法確認匯入保存狀態；請保留輸入、排除 I/O 錯誤後重試或查詢報告身分。",
            "SESSION_SEAL_IO" => en ? "Session completion is unconfirmed; keep the apply proof, resolve I/O and retry the exact seal request." : "無法確認 session 封存狀態；請保留套用證明、排除 I/O 錯誤後重送相同 seal 請求。",
            _ => en ? "The operation failed." : "操作失敗。"
        };
    }
}
