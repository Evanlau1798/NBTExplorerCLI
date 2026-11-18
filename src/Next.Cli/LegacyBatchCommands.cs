using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class LegacyBatchCommands
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static CliResult Execute(CliArguments request)
    {
        var root = request.Get("world") ?? throw new CliFailure("INVALID_ARGUMENTS", "Legacy import requires an explicit --world binding");
        var report = Path.GetFullPath(request.Get("migration-report") ?? throw new CliFailure("INVALID_ARGUMENTS", "Legacy import requires --migration-report outside the world"));
        var preview = request.LegacyBatchPreview;
        var retry = preview && File.Exists(report);
        var context = new WriteContext(root, request, verifyExistingExport: retry);
        ScanCommands.OutsideWorld(context.Root, report);
        if (!retry) QueryExportFile.EnsureNew(report);
        if (request.Get("output") is { } output && Path.GetFullPath(output).Equals(report, StringComparison.OrdinalIgnoreCase)) throw new CliFailure("INVALID_ARGUMENTS", "Plan output and migration report require distinct destinations");
        var raw = CliInput.ReadBytes(request.Input!, LegacyBatchJson.MaximumInputBytes); var input = LegacyBatchJson.Read(raw);
        var mappings = new List<LegacyBatchMapping>(); var operations = new List<PatchOperation>(); var problem = input.Problem;
        if (problem is null)
        {
            try
            { if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.WorldPath!)).Equals(context.Root, StringComparison.OrdinalIgnoreCase)) problem = "LegacyWorldBindingMismatch"; }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { problem = "InvalidLegacyWorldPath"; }
        }
        foreach (var item in input.Operations)
        {
            var id = "legacy-" + item.Index.ToString(CultureInfo.InvariantCulture); SourceAddress? address = null; var reason = problem ?? item.Problem; var decision = "rejected";
            if (reason is null)
            {
                try
                {
                    using var source = Source(context, item.Path!); var selected = Select(source.Root ?? throw new NotSupportedException("Legacy mapping requires a typed NBT document"), source.LegacyPath!);
                    if (selected.Node.Nbt is null) throw new NotSupportedException("Legacy native JSON changes require versioned operations");
                    var replacement = Value(selected.Node.Nbt, item.Value!); address = source.Address(selected.Path);
                    operations.Add(new TypedPatch(id, address, TypedEdit.Replace(selected.Path, selected.Node.Nbt, replacement))); decision = "mapped";
                }
                catch (Exception error) when (error is CliFailure or EditRejectedException or ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or CodecException or JsonException)
                { reason = CliApplication.Code(error); decision = reason is "CONFLICT" or "AMBIGUOUS_PATH" ? "conflict" : reason == "UNSUPPORTED_SOURCE" ? "unsupported" : "rejected"; }
            }
            mappings.Add(new(item.Index, id, item.Id, item.Path, item.LineIndex, address, decision, reason, item.Diagnostics));
        }
        var mapped = problem is null && mappings.All(item => item.Decision == "mapped");
        CliResult? proposal = null; Exception? previewFailure = null;
        if (preview && mapped)
        {
            // Bind report replay to the exact plan and preimages before exporting either file.
            try { proposal = WriteCommands.ExecuteOperations(context, operations, false); }
            catch (Exception error) { previewFailure = error; }
        }
        var previewHash = proposal?.Export is { } plan ? Hash256.Compute(Utf8.GetBytes(plan)) : null;
        var reportJson = LegacyBatchJson.Report(raw, context.Inventory.WorldId, input, mappings, problem, previewHash);
        QueryExportFile.WriteNew(report, reportJson, acceptIdentical: preview);
        var summary = new { inputHash = Hash256.Compute(raw), reportHash = Hash256.Compute(Utf8.GetBytes(reportJson)), requested = input.Operations.Length, mapped = operations.Count };
        if (!mapped)
            throw new CliFailure(FailureCode(problem, mappings),
                "Legacy batch mapping refused; retain the report and resolve each mapping before creating a fresh plan", new { migration = summary, repair = "inspect-migration-report-and-rescan", result = CommandOutputJson.RejectedOperations(mappings.Select(item => item.OperationId).ToArray(), "LegacyMappingRejected") });
        Hash256? convenienceBackupHash = null;
        try
        {
            if (previewFailure is not null) ExceptionDispatchInfo.Throw(previewFailure);
            if (!preview && input.CreateBackup)
            {
                var backup = WriteCommands.Execute(new("backup", context.Root, new Dictionary<string, string?> { ["state-root"] = context.State }, request.Locale));
                if (backup.Code is not null) throw new CliFailure(backup.Code, "Convenience backup failed before world commit", backup.Data);
                convenienceBackupHash = Hash256.Compute(Utf8.GetBytes(backup.Export!));
            }
            var result = proposal ?? WriteCommands.ExecuteOperations(context, operations, !preview);
            return result with { Data = new { result = result.Data, migration = summary, convenienceBackupHash } };
        }
        catch (CliFailure error)
        { throw new CliFailure(error.Code, error.Message, new { result = error.Result, migration = summary, convenienceBackupHash, repair = "retain-report-and-inspect-operation-results" }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or CodecException or JsonException or NotSupportedException)
        { throw new CliFailure(CliApplication.Code(error), error.Message, new { migration = summary, convenienceBackupHash, repair = "retain-report-and-inspect-backup-or-apply-status" }); }
    }
    private static string FailureCode(string? problem, IReadOnlyList<LegacyBatchMapping> items)
    {
        if (problem is not null) return problem switch { "LegacyWorldBindingMismatch" => "CONFLICT", "InvalidLegacyBatchJson" => "MALFORMED_SOURCE", "LegacyInputLimit" => "RESOURCE_LIMIT", _ => "INVALID_ARGUMENTS" };
        var reason = items.First(item => item.Decision != "mapped").Reason;
        return reason is "RESOURCE_LIMIT" or "READ_ERROR" or "ACCESS_DENIED" or "FILE_NOT_FOUND" or "NOT_FOUND" or "MALFORMED_SOURCE" or "INVALID_DATA" or "INCOMPLETE_SOURCE" or "UNSUPPORTED_SOURCE" or "CONFLICT" or "AMBIGUOUS_PATH" ? reason : "INVALID_ARGUMENTS";
    }
    private static QuerySource Source(WriteContext context, string path)
    {
        var options = new Dictionary<string, string?>(); string artifact; string node;
        var separator = path.IndexOf('#');
        if (separator > 0)
        {
            artifact = path[..separator]; node = path[(separator + 1)..];
            if (!artifact.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Legacy # mapping requires a DAT source; use exact versioned addresses for other sources");
        }
        else
        {
            var normalized = path.Replace('\\', '/');
            var extension = normalized.IndexOf(".mca/", StringComparison.OrdinalIgnoreCase); if (extension < 0) extension = normalized.IndexOf(".mcr/", StringComparison.OrdinalIgnoreCase);
            if (extension < 0) throw new NotSupportedException("Unrecognized legacy artifact/chunk locator");
            artifact = path[..(extension + 4)]; var tail = path[(extension + 5)..];
            if (tail.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase)) tail = tail[5..].TrimStart();
            var end = tail.IndexOf(']');
            if (!tail.StartsWith('[') || end < 0 || end + 1 >= tail.Length || tail[end + 1] is not ('/' or '\\')) throw new CliFailure("INVALID_ARGUMENTS", "Legacy chunk coordinates require [x,z]/node");
            var coordinates = tail[1..end].Split(',');
            if (coordinates.Length != 2 || !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) || !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z)) throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy chunk coordinates");
            options["chunk"] = x.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture); node = tail[(end + 2)..];
            artifact = artifact.Replace('\\', '/');
            if (!artifact.Contains('/'))
            {
                var catalog = context.Inventory.Enumerate();
                if (catalog.DiagnosticCode is not null || catalog.Entries.Any(entry => entry.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported)) throw new CliFailure("INCOMPLETE_SOURCE", "Legacy basename mapping requires a complete world catalog");
                var matches = catalog.Entries.Where(entry => !entry.IsDirectory && Path.GetFileName(entry.RelativePath).Equals(artifact, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (matches.Length != 1) throw new CliFailure("CONFLICT", "Legacy region basename is absent or ambiguous; use its complete path after rescanning");
                artifact = matches[0].RelativePath;
            }
        }
        artifact = artifact.Replace('\\', '/'); ArtifactAddress.ValidatePath(artifact, false);
        if (node.Length == 0) throw new CliFailure("INVALID_ARGUMENTS", "Legacy node path is empty");
        options["path"] = artifact + "#" + node;
        return new(new("set", context.Root, options, "zh-TW"));
    }
    private static (QueryNode Node, TypedPath Path) Select(QueryNode root, string path)
    {
        var node = root; var segments = new List<PathSegment>(); var cursor = 0;
        void Step(PathSegment segment)
        { if (segments.Count == 256) throw new CliFailure("RESOURCE_LIMIT", "Legacy node path is too deep"); node = node.Child(segment); segments.Add(segment); }
        while (cursor < path.Length)
        {
            var remaining = path[cursor..];
            if (node.Nbt is NbtCompound compound && compound.Entries.Any(entry => entry.Name.IndexOfAny(['/', '\\', '[', ']']) >= 0
                && remaining.StartsWith(entry.Name, StringComparison.Ordinal) && (remaining.Length == entry.Name.Length || remaining[entry.Name.Length] is '/' or '\\' or '[')))
                throw new CliFailure("AMBIGUOUS_PATH", "Legacy separators also form a literal key; rescan and use typed segments");
            var start = cursor; while (cursor < path.Length && path[cursor] is not ('/' or '\\' or '[' or ']')) cursor++;
            if (cursor > start) Step(PathSegment.ForKey(path[start..cursor]));
            else if (path[cursor] != '[') throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy node path");
            while (cursor < path.Length && path[cursor] == '[')
            {
                var end = path.IndexOf(']', cursor + 1); var number = end < 0 ? "" : path[(cursor + 1)..end];
                if (number.Length == 0 || !number.All(char.IsAsciiDigit) || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy array index");
                Step(PathSegment.ForIndex(index)); cursor = end + 1;
            }
            if (cursor == path.Length) break;
            if (path[cursor++] is not ('/' or '\\') || cursor == path.Length) throw new CliFailure("INVALID_ARGUMENTS", "Invalid legacy path separator");
        }
        return (node, new(segments));
    }
    private static NbtValue Value(NbtValue current, string text)
    {
        if (current is NbtString) return new NbtString(text);
        // shortcut: non-string legacy values require the versioned typed document, extend only with a proven lossless legacy profile.
        using var json = BoundedJson.ReadDecoded(new System.Text.UTF8Encoding(false, true).GetBytes(text));
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("schemaFamily", out var family) || family.ValueKind != JsonValueKind.String || NativeJsonStrings.Read(family) != TypedNbtJson.Family)
            throw new NotSupportedException("A non-string legacy target needs a complete versioned typed-NBT Value; raw JSON type inference is unavailable");
        return TypedNbtJson.Read(text).Root;
    }
}
