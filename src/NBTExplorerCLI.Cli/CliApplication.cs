using System.Text.Json;
using System.Reflection;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;

namespace NBTExplorerCLI.Cli;

public static class CliApplication
{
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        var command = args.Count == 0 ? "help" : args[0];
        try
        {
            var rest = CliArguments.StripUiLocale(args, out command);
            var request = CliArguments.Parse(rest); command = request.Command;
            object data;
            if (command == "help" || request.Flag("help") || request.Subcommand == "help")
            {
                var subject = command == "help" ? request.Input : command;
                if (subject is not null) subject = CliArguments.Canonical(subject);
                data = new { usage = "nbtcli <command> [options]", commands = CliArguments.Commands,
                    subject, subcommand = request.Subcommand, subcommands = subject switch { "translate" => SessionCommands.Subcommands, "agent" => AgentCommands.Subcommands, _ => Array.Empty<string>() },
                    options = (subject is null ? Array.Empty<string>() : CliArguments.Allowed(subject, request.Subcommand)).Select(option => "--" + option).Append("--ui-locale").ToArray() };
                command = "help";
            }
            else if (command == "version") data = new { product = "nbtcli", version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] };
            else if (command is "scan" or "extract" or "validate" or "capabilities" or "set" or "batch" or "plan" or "apply" or "recover" or "status" or "backup" or "restore" or "convert" or "translate" or "agent")
            {
                var result = command == "convert" ? ConvertCommands.Execute(request) : command == "agent" ? AgentCommands.Execute(request) : command == "translate" ? SessionCommands.Execute(request) : command is "scan" or "extract" or "validate" or "capabilities" ? ScanCommands.Execute(request) : WriteCommands.Execute(request);
                var json = CommandOutputJson.Write(command, result.Data, result.Outcome, result.Code, result.Code is null ? null : Message(result.Code));
                var export = request.Command == "translate" && request.Subcommand is "export-lang" or "create-pack" ? request.Get("plan-output") : request.Get("output");
                if (export is { } file)
                {
                    try
                    {
                        QueryExportFile.WriteNew(file, result.Export ?? (json + Environment.NewLine),
                            request.Command == "translate" && request.Subcommand == "fallback" ? FallbackCommands.CheckExport : null);
                    }
                    catch (ArtifactConflictException error)
                    {
                        output.WriteLine(CommandOutputJson.Write(command, result.Data, "Conflict", "CONFLICT", Message("CONFLICT"), error.Message)); return 3;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                    {
                        output.WriteLine(CommandOutputJson.Write(command, result.Data, "Failed", "EXPORT_FAILED", Message("EXPORT_FAILED"), error.Message)); return 5;
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
            output.WriteLine(CommandOutputJson.Write(CliArguments.Canonical(command), (error as CliFailure)?.Result, outcome, code, Message(code), error.Message)); return exit;
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
        ArtifactSizeLimitException or JsonOutputLimitException or SessionPageOversizedException or SessionSlotLimitException or CodecException { Code: CodecError.LimitExceeded } => "RESOURCE_LIMIT",
        CodecException { Code: CodecError.UnsupportedProfile } or NotSupportedException => "UNSUPPORTED_SOURCE",
        CodecException or JsonException => "MALFORMED_SOURCE",
        InvalidDataException => "INVALID_DATA",
        IOException => "READ_ERROR",
        ArgumentException => "INVALID_ARGUMENTS",
        _ => "INTERNAL_ERROR"
    };
    private static string Message(string code) => code switch
    {
        "UNKNOWN_COMMAND" => "Unknown command.",
        "UNKNOWN_OPTION" => "Unknown option.",
        "INVALID_ARGUMENTS" or "CHUNK_REQUIRED" => "Invalid or missing arguments.",
        "FILE_NOT_FOUND" => "File or directory not found.",
        "NOT_FOUND" => "The exact target was not found.",
        "AMBIGUOUS_PATH" or "CONFLICT" => "The target is ambiguous or has changed.",
        "UNSUPPORTED_SOURCE" or "INCOMPLETE_SOURCE" => "The source is unsupported or incomplete.",
        "MALFORMED_SOURCE" => "The source data is malformed.",
        "INVALID_DATA" => "The data failed validation.",
        "ACCESS_DENIED" => "Access denied.",
        "RESOURCE_LIMIT" => "The resource limit was exceeded.",
        "PATTERN_TIMEOUT" => "The match pattern timed out.",
        "EXPORT_FAILED" => "The export failed; inspect the operation results.",
        "RECOVERY_REQUIRED" => "Recovery is required; inspect every operation result.",
        "PREPARATION_FAILED" => "Preparation failed before commit; inspect validation results.",
        "ROLLED_BACK" => "The plan was rolled back.",
        "READ_ERROR" => "The source could not be read.",
        "AGENT_STATE_IO" => "Agent state could not be confirmed; retry the exact questionnaire after resolving the I/O error.",
        "SESSION_SEAL_IO" => "Session completion is unconfirmed; keep the apply proof, resolve I/O and retry the exact seal request.",
        _ => "The operation failed."
    };
}
