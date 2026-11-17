using System.Text.Json;
using System.Globalization;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;

namespace Next.Cli;

internal static class WriteCommands
{
    internal static CliResult Execute(CliArguments request)
    {
        var write = request.Command is "apply" or "recover" || request.Flag("apply");
        if (write && !request.Flag("world-offline")) throw new CliFailure("INVALID_ARGUMENTS", "Writing requires --world-offline after closing Minecraft and other writers");
        if (request.Command == "batch" && request.Flag("legacy")) return LegacyBatchCommands.Execute(request);
        if (request.Flag("migration-report")) throw new CliFailure("INVALID_ARGUMENTS", "--migration-report requires explicit --legacy");
        if (request.Command == "set" && (request.Flag("value") == request.Flag("value-file"))) throw new CliFailure("INVALID_ARGUMENTS", "Choose exactly one --value or --value-file typed-NBT document");
        var suffix = request.Get("suffix");
        if (suffix is not null)
        {
            if (string.IsNullOrWhiteSpace(suffix) || suffix.Length > 64 || suffix.Contains('/')) throw new CliFailure("INVALID_ARGUMENTS", "Backup suffix must be one filename segment of 1–64 UTF-16 units");
            ArtifactAddress.ValidatePath(suffix, false);
        }
        var root = request.Command == "batch" ? Required(request, "world") : request.Input!;
        var context = new WriteContext(root, request);
        if (request.Command is "status" or "recover")
        {
            var hash = new Hash256(Required(request, "plan-hash"));
            var engine = context.Engine();
            if (request.Command == "status") return WriteExecution.Status(engine, hash, context.Root, context.State);
            return Recover(context, engine, hash, request.Flag("rollback"));
        }
        if (request.Command == "backup")
        {
            ArtifactAddress[] artifacts;
            if (request.Get("path") is { } path) artifacts = [context.Artifact(path)];
            else
            {
                var catalog = context.Inventory.Enumerate();
                if (catalog.DiagnosticCode is not null || catalog.Entries.Any(entry => entry.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported))
                    return ScanProjection.Classify(new { catalog.DiagnosticCode, sources = catalog.Entries.Where(entry => entry.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported).Take(100).ToArray() }, false,
                        ReadCommands.IsResourceLimit(catalog.DiagnosticCode) || catalog.Entries.Any(entry => ReadCommands.IsResourceLimit(entry.DiagnosticCode)),
                        ScanProjection.ReadFailure(catalog.DiagnosticCode) || catalog.Entries.Any(entry => ScanProjection.ReadFailure(entry.DiagnosticCode)),
                        catalog.Entries.Any(entry => entry.Outcome == SourceOutcome.Failed), catalog.Entries.Any(entry => entry.Outcome == SourceOutcome.Unsupported));
                artifacts = catalog.Entries.Where(entry => !entry.IsDirectory && !Path.GetFileName(entry.RelativePath).StartsWith(".nbtcli-", StringComparison.OrdinalIgnoreCase)).Select(entry => context.Artifact(entry.RelativePath)).ToArray();
            }
            var backup = new WorldBackupService(context.Inventory, context.Objects()).Create(artifacts.Select(artifact => new ArtifactPrecondition(artifact, context.Image(artifact))));
            var json = BackupManifestJson.Write(backup.Manifest);
            var backupPath = suffix is null ? null : Path.Combine(context.State, "backup_" + suffix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfffffff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N") + ".json");
            object Data(bool published) => new { manifestHash = backup.ManifestHash, manifest = Json(json), stateRoot = context.State, backupPath, namedManifestPublished = published };
            if (backupPath is not null)
            {
                try { QueryExportFile.WriteNew(backupPath, json); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { throw new CliFailure("EXPORT_FAILED", error.Message, Data(false)); }
            }
            return new(Data(backupPath is not null), Export: json);
        }
        if (request.Command == "restore")
        {
            var backup = new WorldBackupService(context.Inventory, context.Objects()).Load(new(Required(request, "manifest-hash")));
            var selected = backup.Manifest.Artifacts.Where(artifact => request.Get("path") is not { } path || artifact.Artifact.RelativePath == path).ToArray();
            if (selected.Length == 0) throw new CliFailure("NOT_FOUND", "Requested artifact is absent from the verified backup");
            var plan = BeforeCommit(["restore-0"], "PlanRejected", () =>
            {
                var restore = new RestorePatch("restore-0", new(backup.ManifestHash, selected, selected.Select(artifact => new KeyValuePair<ArtifactAddress, ArtifactImage>(artifact.Artifact, context.Image(artifact.Artifact)))));
                return new WorldPlanBuilder(context.Inventory).Build(backup, [restore]);
            });
            if (!write) return Preview(plan);
            using var prepared = Prepare(plan.Operations, () => new WorldPlanPreparation(context.Inventory, context.Storage).Prepare(backup, plan, true));
            var restoreEngine = context.Engine(); return Apply(prepared, restoreEngine, context.Root, context.State);
        }
        if (request.Command == "apply")
        {
            var plan = PatchPlanJson.Read(CliInput.Read(Required(request, "plan"))); var engine = context.Engine();
            var ids = plan.Operations.Select(operation => operation.Id).ToArray();
            CommandOutputJson.ValidateRejectionBudget(ids);
            if (plan.WorldId != context.Inventory.WorldId) throw Rejected(ids, new ArtifactConflictException("Plan belongs to another world root"), "PlanRejected");
            if (WriteExecution.TryReplay(engine, plan, context.Root, context.State) is { } replay) return replay;
            _ = BeforeCommit(ids, "PlanRejected", () =>
            {
                foreach (var input in plan.Preconditions) if (context.Image(input.Artifact) != input.Image) throw new ArtifactConflictException("Plan preimage no longer matches the exact source");
                return plan;
            });
            if (!plan.Operations.IsEmpty && plan.Operations.All(operation => operation is RestorePatch))
            {
                var backup = BeforeCommit(ids, "PlanRejected", () =>
                {
                    var hashes = plan.Operations.Cast<RestorePatch>().Select(operation => operation.Restore.VerifiedManifestHash).Distinct().ToArray();
                    if (hashes.Length != 1) throw new NotSupportedException("Raw restore requires one verified backup manifest");
                    return new WorldBackupService(context.Inventory, context.Objects()).Load(hashes[0]);
                });
                using var prepared = Prepare(plan.Operations, () => new WorldPlanPreparation(context.Inventory, context.Storage).Prepare(backup, plan, true));
                return Apply(prepared, engine, context.Root, context.State);
            }
            if (plan.Operations.Any(operation => operation is ResourcePatch))
            {
                using var resource = Prepare(plan.Operations, () => new ResourceOutputService(context.Inventory, context.Storage, context.Objects()).Prepare(plan, true));
                return Apply(resource, engine, context.Root, context.State);
            }
            using var candidate = Prepare(plan.Operations, () => new WorldPlanPreparation(context.Inventory, context.Storage).Prepare(context.Query(SourceIdentity.ReadSnapshotScope(plan.SnapshotId)), plan, true));
            return Apply(candidate, engine, context.Root, context.State);
        }
        IReadOnlyList<PatchOperation> operations;
        if (request.Command == "set")
        {
            using var source = new QuerySource(request);
            if (source.Artifact.WorldId != context.Inventory.WorldId || source.Root?.Nbt is null) throw new NotSupportedException("Typed set requires an NBT artifact in the selected world");
            var selected = QueryPath.Select(source.Root, request, source.LegacyPath);
            var value = TypedNbtJson.Read(request.Get("value") ?? CliInput.Read(Required(request, "value-file"))).Root;
            operations = [new TypedPatch(request.Get("operation-id") ?? "edit-0", source.Address(selected.Path), TypedEdit.Replace(selected.Path, selected.Node.Nbt!, value))];
        }
        else operations = OperationSetJson.Read(CliInput.Read(request.Command == "batch" ? request.Input! : Required(request, "request")));
        return ExecuteOperations(context, operations, write);
    }
    internal static CliResult ExecuteOperations(WriteContext context, IReadOnlyList<PatchOperation> operations, bool write, WorldQuerySnapshot? suppliedSnapshot = null)
    {
        CommandOutputJson.ValidateRejectionBudget(operations.Select(operation => operation.Id).ToArray());
        var bound = BeforeCommit(operations.Select(operation => operation.Id).ToArray(), "PlanRejected", () =>
        {
            var snapshot = suppliedSnapshot ?? context.Query(); var outputs = operations.SelectMany(operation => operation.RequiredOutputs).ToHashSet();
            var blocked = snapshot.Manifest.InputSources.FirstOrDefault(source => outputs.Contains(source.Artifact) && source.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported or SourceOutcome.Cancelled);
            if (blocked is not null)
            {
                var code = ReadCommands.IsResourceLimit(blocked.DiagnosticCode) ? "RESOURCE_LIMIT"
                    : ScanProjection.ReadFailure(blocked.DiagnosticCode) || ScanProjection.ReadFailure(snapshot.Manifest.CatalogDiagnosticCode) ? "READ_ERROR"
                    : blocked.Outcome == SourceOutcome.Unsupported ? "UNSUPPORTED_SOURCE" : "MALFORMED_SOURCE";
                throw new CliFailure(code, blocked.DiagnosticCode);
            }
            return (Snapshot: snapshot, Plan: new WorldPlanBuilder(context.Inventory).Build(snapshot, operations));
        });
        if (!write) return Preview(bound.Plan);
        using var output = Prepare(bound.Plan.Operations, () => new WorldPlanPreparation(context.Inventory, context.Storage).Prepare(bound.Snapshot, bound.Plan, true));
        var applyEngine = context.Engine(); return Apply(output, applyEngine, context.Root, context.State);
    }
    private static CliResult Preview(PatchPlan plan) => new(new { preview = true, plan = Json(PatchPlanJson.Write(plan)) }, Export: PatchPlanJson.Write(plan));
    private static PreparedWorldPlan Prepare(IReadOnlyList<PatchOperation> operations, Func<PreparedWorldPlan> prepare)
        => BeforeCommit(operations.Select(operation => operation.Id).ToArray(), "PreparationRejected", prepare);
    internal static T BeforeCommit<T>(IReadOnlyList<string> operationIds, string phase, Func<T> execute)
    {
        try { return execute(); }
        catch (Exception error)
        { throw Rejected(operationIds, error, phase); }
    }
    private static CliFailure Rejected(IReadOnlyList<string> operationIds, Exception error, string phase)
    {
        var code = CliApplication.Code(error); if (phase == "PreparationRejected" && code == "READ_ERROR") code = "PREPARATION_FAILED";
        return new(code, error.Message, CommandOutputJson.RejectedOperations(operationIds, phase));
    }
    internal static CliResult Apply(PreparedWorldPlan prepared, WorldApplyEngine engine, string root, string state)
        => WriteExecution.Apply(prepared, engine, root, state);
    private static CliResult Recover(WriteContext context, WorldApplyEngine engine, Hash256 hash, bool rollback = false)
        => WriteExecution.Recover(engine, hash, context.Root, context.State, rollback);
    internal static CliResult Status(WorldApplyStatus? state, ApplyReceipt? fallback = null)
    {
        var receipt = state?.Receipt ?? fallback;
        if (state is null && receipt is null) throw new FileNotFoundException("No journal for the requested plan");
        var outcomes = receipt?.Results.Select(result => result.Outcome).ToHashSet() ?? [];
        var complete = state?.Phase == ApplyJournalPhase.Completed || state is null && receipt?.Requested == 0;
        var rollback = complete && state?.Direction == ApplyDirection.Rollback && receipt?.DependenciesComplete == true && outcomes.All(outcome => outcome is OperationOutcome.RolledBack or OperationOutcome.Unchanged);
        var exit = rollback ? 8 : receipt is null || !complete && !outcomes.Overlaps([OperationOutcome.Conflict, OperationOutcome.Cancelled, OperationOutcome.Failed, OperationOutcome.Unsupported]) ? 7
            : complete && receipt.Success ? 0 : outcomes.Contains(OperationOutcome.RecoveryRequired) ? 7 : outcomes.Contains(OperationOutcome.Conflict) ? 3
            : outcomes.Contains(OperationOutcome.Cancelled) ? 130 : outcomes.Contains(OperationOutcome.Unsupported) ? 4 : 5;
        var code = exit switch { 0 => null, 3 => "CONFLICT", 4 => "UNSUPPORTED_SOURCE", 7 => "RECOVERY_REQUIRED", 8 => "ROLLED_BACK", 130 => "CANCELLED", _ => "APPLY_FAILED" };
        var json = receipt is null ? null : ApplyReceiptJson.Write(receipt);
        return new(new { planHash = state?.PlanHash ?? receipt!.PlanHash, direction = state?.Direction, phase = state?.Phase, sequence = state?.Sequence,
            backupManifestHash = state?.BackupManifestHash, receiptIsTerminal = complete, receipt = json is null ? (JsonElement?)null : Json(json) },
            code, exit == 0 ? "Success" : exit == 7 ? "RecoveryRequired" : exit == 8 ? "RolledBack" : exit == 3 ? "Conflict" : exit == 4 ? "Unsupported" : exit == 130 ? "Cancelled" : "Failed", exit, json);
    }
    private static JsonElement Json(string value) { using var parsed = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 1024 }); return parsed.RootElement.Clone(); }
    private static string Required(CliArguments request, string option) => request.Get(option) ?? throw new CliFailure("INVALID_ARGUMENTS", "Missing --" + option);
}
