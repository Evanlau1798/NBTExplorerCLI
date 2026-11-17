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
            if (command == "help" || request.Flag("help"))
            {
                var subject = command == "help" ? request.Input : command;
                if (subject is not null) subject = CliArguments.Canonical(subject);
                data = new { usage = "nbtcli-next <command> [options]", commands = CliArguments.Commands,
                    subject, options = (subject is null ? Array.Empty<string>() : CliArguments.Allowed(subject)).Select(option => "--" + option).Append("--ui-locale").ToArray() };
                command = "help";
            }
            else if (command == "version") data = new { product = "nbtcli-next", version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] };
            else data = ReadCommands.Execute(request);
            output.WriteLine(CommandOutputJson.Write(command, data)); return 0;
        }
        catch (Exception error)
        {
            var code = error switch
            {
                CliFailure failure => failure.Code,
                ArtifactConflictException => "CONFLICT",
                FileNotFoundException or DirectoryNotFoundException => "FILE_NOT_FOUND",
                UnauthorizedAccessException => "ACCESS_DENIED",
                ArtifactSizeLimitException or JsonOutputLimitException => "RESOURCE_LIMIT",
                CodecException { Code: CodecError.LimitExceeded } => "RESOURCE_LIMIT",
                CodecException { Code: CodecError.UnsupportedProfile } or NotSupportedException => "UNSUPPORTED_SOURCE",
                CodecException or JsonException => "MALFORMED_SOURCE",
                IOException => "READ_ERROR",
                ArgumentException => "INVALID_ARGUMENTS",
                _ => "INTERNAL_ERROR"
            };
            var exit = code switch { "UNKNOWN_COMMAND" or "UNKNOWN_OPTION" or "INVALID_ARGUMENTS" or "CHUNK_REQUIRED" => 2, "CONFLICT" or "AMBIGUOUS_PATH" => 3, "UNSUPPORTED_SOURCE" or "INCOMPLETE_SOURCE" => 4, "RESOURCE_LIMIT" => 6, "INTERNAL_ERROR" => 1, _ => 5 };
            var outcome = exit == 3 ? "Conflict" : exit == 4 ? "Unsupported" : "Failed";
            output.WriteLine(CommandOutputJson.Write(CliArguments.Canonical(command), null, outcome, code, Message(code, locale), error.Message)); return exit;
        }
    }
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
            "ACCESS_DENIED" => en ? "Access denied." : "拒絕存取。",
            "RESOURCE_LIMIT" => en ? "The resource limit was exceeded." : "超過資源上限。",
            "READ_ERROR" => en ? "The source could not be read." : "無法讀取來源。",
            _ => en ? "The operation failed." : "操作失敗。"
        };
    }
}
