using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CliScanTests;

namespace Next.Verification;

internal static class CliSessionWriteTests
{
    private static string State(string work) => Path.Combine(work, "state");
    private static Invocation Call(string work, string world, Guid id, string action, params string[] args) => Run(work,
        ["translate", action, world, "--session-id", id.ToString(), "--state-root", State(work), .. args]);
    private static void Start(string work, string world, Guid id)
    { using var started = Success(Call(work, world, id, "start", "--source-language", "en_us", "--target-language", "zh_tw")); }
    private static long Submit(string work, string world, Guid id, bool unchanged = false)
    {
        using var exported = Success(Call(work, world, id, "export")); var page = SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
        var reply = SessionExchangeJson.ReadReply(exported.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText());
        reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = unchanged ? SessionReplyState.Unchanged : SessionReplyState.Translated,
            Text = unchanged ? null : "譯文:" + page.Slots.Single(item => item.SlotId == slot.SlotId).SourceText }).ToImmutableArray() };
        var file = Path.Combine(work, "reply.json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply), new UTF8Encoding(false));
        using var submitted = Success(Call(work, world, id, "submit", "--reply", file));
        return submitted.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64();
    }
    private static (PatchPlan Plan, string File) Plan(string work, string world, Guid id, long revision)
    {
        var file = Path.Combine(work, "plan-" + Guid.NewGuid().ToString("N") + ".json"); var operation = Guid.NewGuid();
        using var preview = Success(Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--operation-id", operation.ToString(), "--output", file));
        Require(preview.RootElement.GetProperty("data").GetProperty("preview").GetBoolean(), "session planning is an explicit preview");
        return (PatchPlanJson.Read(File.ReadAllText(file)), file);
    }
    private static Invocation Apply(string work, string world, string plan) => Run(work, "apply", world, "--plan", plan, "--world-offline", "--state-root", State(work));
    private static Invocation Seal(string work, string world, Guid id, long revision, Guid operation, Hash256 hash) => Call(work, world, id, "seal",
        "--revision", revision.ToString(), "--operation-id", operation.ToString(), "--plan-hash", hash.Hex);
    private static FileSystemTranslationSessionStore Store(string work) => new(Path.Combine(State(work), "sessions"));
    public static void PureMixedAndUnchangedSessionsUseThePublicPlanApplySealWorkflow()
    {
        foreach (var mode in new[] { "pure", "mixed", "unchanged" })
        {
            var world = mode == "pure" ? WorldQueryTests.CreateWorld() : WorldPreparationTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
            try
            {
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                Start(work, world, id); var revision = Submit(work, world, id, mode == "unchanged"); var store = Store(work); var accepted = store.Read(id)!;
                var (plan, file) = Plan(work, world, id, revision);
                Require(original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && store.Read(id)!.SequenceEqual(accepted), "preview preserves every world byte and the accepted session");
                using var applied = Success(Apply(work, world, file)); var receipt = Engine(world, work, plan.WorldId).ReadStatus(plan.PlanHash)!.Receipt!;
                Require(applied.RootElement.GetProperty("data").GetProperty("receipt").GetRawText() == ApplyReceiptJson.Write(receipt), "public receipt matches the durable prepared journal's verified receipt");
                Require(receipt.Success && receipt.Requested == new TranslationSessionService(store).Read(id).Slots.Length, "one original engine receipt accounts for every selected slot");
                if (mode == "unchanged") Require(receipt.Results.All(result => result.Outcome == OperationOutcome.Unchanged)
                    && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "explicit unchanged replies create no changed world images");
                else
                {
                    var readback = LegacyImportMappingTests.Query(world, Path.Combine(work, "readback"));
                    Require(readback.Manifest.CoverageComplete && readback.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).All(item => item.Text.StartsWith("譯文:", StringComparison.Ordinal)), "new format-aware readback finds every selected translation");
                }
                var operation = Guid.NewGuid(); using var sealedResult = Success(Seal(work, world, id, revision, operation, plan.PlanHash));
                var status = sealedResult.RootElement.GetProperty("data").GetProperty("status");
                Require(status.GetProperty("applied").GetInt32() == receipt.Requested && status.GetProperty("revision").GetInt64() == revision + 1, "only complete durable world proof advances session completion");
                var sealedBytes = store.Read(id)!; using var replay = Success(Seal(work, world, id, revision, operation, plan.PlanHash));
                Require(store.Read(id)!.SequenceEqual(sealedBytes), "exact seal retry retains the original operation receipt without another revision");
                using var history = Success(Call(work, world, id, "status")); var historyData = history.RootElement.GetProperty("data");
                Require(historyData.GetProperty("status").GetProperty("applied").GetInt32() == receipt.Requested
                    && historyData.GetProperty("sourceFresh").ValueKind == JsonValueKind.Null && historyData.GetProperty("freshness").GetString() == "not-checked", "sealed status reports durable history without claiming current world freshness");
                Require(!sealedResult.RootElement.GetProperty("data").GetProperty("completion").TryGetProperty("inputBase64", out _), "public completion does not expose internal plan/AST input");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void PendingStaleRevisionAndChangedSourcesCannotProduceWritablePlans()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id);
            Require(Call(work, world, id, "plan", "--direct", "--revision", "0").Exit == 3, "pending replies cannot produce an apparently writable session plan");
            var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            Require(Call(work, world, id, "plan", "--direct", "--revision", "0").Exit == 3, "caller revision is an exact precondition");
            File.AppendAllText(Path.Combine(world, WorldQueryTests.Function(0)), "# external change\n");
            var output = Path.Combine(work, "stale-plan.json"); var changed = Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--output", output);
            Require(changed.Exit == 3 && !File.Exists(output) && Store(work).Read(id)!.SequenceEqual(before), "changed sources refuse publication and retain all validated replies");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MissingAndSubsetWorldProofsKeepTheSessionUnsealed()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            var missing = Seal(work, world, id, revision, Guid.NewGuid(), Hash256.Compute([1])); using var error = JsonDocument.Parse(missing.Output);
            Require(missing.Exit == 5 && error.RootElement.GetProperty("data").GetProperty("repair").GetString() == "inspect-apply-status-and-retry-seal", "missing journal returns a concrete repair without discarding session work");
            var (plan, _) = Plan(work, world, id, revision); var service = new TranslationSessionService(Store(work)); var selected = service.Read(id).Slots[0];
            var inventory = new WorldInventory(world, plan.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
            var subset = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("subset", new(selected.Slot, selected.Reply!))]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(world, plan.WorldId)).Prepare(snapshot, subset, true);
            var engine = Engine(world, work, plan.WorldId); Require(engine.Apply(prepared, true).Success, "control: a legitimate subset world apply has a successful durable receipt");
            Require(Seal(work, world, id, revision, Guid.NewGuid(), subset.PlanHash).Exit == 3 && Store(work).Read(id)!.SequenceEqual(before), "a successful subset receipt cannot seal omitted translations");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private static WorldApplyEngine Engine(string world, string work, Guid worldId, IArtifactStorage? storage = null) => new(new WorldInventory(world, worldId), storage ?? new ArtifactStorage(world, worldId),
        new FileSystemWorldBackupStore(Path.Combine(State(work), "objects")), new FileSystemApplyJournalStore(Path.Combine(State(work), "journals")));
    public static void ABlockedSessionSealPreservesCompletedWorldsAndOffersExactRetry()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); var revision = Submit(work, world, id); var (plan, file) = Plan(work, world, id, revision); using var applied = Success(Apply(work, world, file));
            var sessionFile = Path.Combine(State(work), "sessions", "session-" + id.ToString("N") + ".json"); var before = File.ReadAllBytes(sessionFile); var operation = Guid.NewGuid(); Invocation blocked;
            using (var locked = File.Open(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read)) blocked = Seal(work, world, id, revision, operation, plan.PlanHash);
            using var failure = JsonDocument.Parse(blocked.Output);
            Require(blocked.Exit == 5 && failure.RootElement.GetProperty("error").GetProperty("code").GetString() == "SESSION_SEAL_IO"
                && failure.RootElement.GetProperty("data").GetProperty("operationId").GetGuid() == operation
                && failure.RootElement.GetProperty("data").GetProperty("repair").GetString() == "retry-same-seal" && File.ReadAllBytes(sessionFile).SequenceEqual(before), "native blocked state publication returns exact retry identities and preserves accepted replies");
            using var sealedResult = Success(Seal(work, world, id, revision, operation, plan.PlanHash)); var sealedBytes = File.ReadAllBytes(sessionFile);
            using var replay = Success(Seal(work, world, id, revision, operation, plan.PlanHash)); Require(File.ReadAllBytes(sessionFile).SequenceEqual(sealedBytes), "unlocking permits exactly one completion");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void PartialApplyAndRollbackCannotSealUnfinishedSessionWork()
    {
        foreach (var rollback in new[] { false, true })
        {
            var world = WorldPreparationTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
            try
            {
                Start(work, world, id); var revision = Submit(work, world, id); var (plan, _) = Plan(work, world, id, revision); var before = Store(work).Read(id)!;
                var inventory = new WorldInventory(world, plan.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                    .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
                var storage = new ArtifactStorage(world, plan.WorldId, new WorldApplyTests.FailReplace(2, true));
                using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
                Require(!Engine(world, work, plan.WorldId, storage).Apply(prepared, true).Success, "controlled native second-replace failure leaves an unfinished journal");
                var operation = Guid.NewGuid(); Require(Seal(work, world, id, revision, operation, plan.PlanHash).Exit == 7 && Store(work).Read(id)!.SequenceEqual(before), "partial proof cannot seal or delete validated replies");
                var recovered = Run(work, ["recover", world, "--plan-hash", plan.PlanHash.Hex, "--world-offline", "--state-root", State(work), .. (rollback ? new[] { "--rollback" } : Array.Empty<string>())]);
                Require(recovered.Exit == (rollback ? 8 : 0), "public recovery preserves forward/rollback outcome distinctions");
                var completion = Seal(work, world, id, revision, operation, plan.PlanHash);
                Require(completion.Exit == (rollback ? 8 : 0), "only forward complete recovery can seal the same request");
                if (rollback)
                {
                    Require(Store(work).Read(id)!.SequenceEqual(before), "rollback keeps translated session work available");
                    var (freshPlan, freshFile) = Plan(work, world, id, revision); Require(freshPlan.PlanHash != plan.PlanHash, "new plan operation identity permits an explicit fresh attempt after terminal rollback");
                    using var reapplied = Success(Apply(work, world, freshFile)); using var resealed = Success(Seal(work, world, id, revision, Guid.NewGuid(), freshPlan.PlanHash));
                }
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void ArgumentsForeignWorldsAndExistingExportsNeverConsumeSessionWork()
    {
        var world = WorldQueryTests.CreateWorld(); var otherWorld = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Require(Call(work, world, id, "plan", "--direct").Exit == 2 && !Directory.Exists(State(work)), "missing revision rejects before repositories are created");
            Start(work, world, id); var revision = Submit(work, world, id); var before = Store(work).Read(id)!;
            Require(Call(work, world, id, "plan", "--revision", revision.ToString()).Exit == 2
                && Call(work, world, id, "plan", "--direct", "--revision", "18446744073709551615").Exit == 2, "mode and bounded revision are explicit");
            Require(Call(work, world, id, "seal", "--revision", revision.ToString(), "--plan-hash", Hash256.Compute([1]).Hex).Exit == 2, "seal never invents a missing retry operation identity");
            Require(Call(work, otherWorld, id, "plan", "--direct", "--revision", revision.ToString()).Exit == 3
                && Seal(work, otherWorld, id, revision, Guid.NewGuid(), Hash256.Compute([1])).Exit == 3, "byte-identical foreign worlds cannot consume session authority");
            var output = Path.Combine(work, "keep-plan.json"); File.WriteAllText(output, "user content");
            Require(Call(work, world, id, "plan", "--direct", "--revision", revision.ToString(), "--output", output).Exit == 3
                && File.ReadAllText(output) == "user content" && Store(work).Read(id)!.SequenceEqual(before), "existing exports and all accepted session bytes remain untouched");
            using var help = Success(Run(work, "translate", "plan", "--help"));
            Require(help.RootElement.GetProperty("data").GetProperty("options").EnumerateArray().Any(item => item.GetString() == "--direct"), "public machine help describes the new plan contract");
        }
        finally { Directory.Delete(world, true); Directory.Delete(otherWorld, true); Directory.Delete(work, true); }
    }
    public static void PublicLegacyImportFlowsThroughPlanApplyAndSealWithoutRebuildingNbt()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world); var raw = LegacyImportMappingTests.Input(); var input = Path.Combine(work, "legacy.json"); File.WriteAllBytes(input, raw);
            using var imported = Success(Run(work, "import-legacy", world, "--input", input, "--profile", "command-session", "--source-language", "en_us", "--target-language", "zh_tw",
                "--acknowledge-world-rebind", "--create-session", "--state-root", State(work)));
            var data = imported.RootElement.GetProperty("data"); var id = data.GetProperty("sessionId").GetGuid(); var revision = data.GetProperty("status").GetProperty("revision").GetInt64();
            var (plan, file) = Plan(work, world, id, revision); using var applied = Success(Apply(work, world, file));
            using var completed = Success(Seal(work, world, id, revision, Guid.NewGuid(), plan.PlanHash));
            Require(completed.RootElement.GetProperty("data").GetProperty("status").GetProperty("applied").GetInt32() == 1
                && File.ReadAllBytes(input).SequenceEqual(raw), "all public commands preserve legacy input while completing one validated translation");
            var readback = LegacyImportMappingTests.Query(world, Path.Combine(work, "readback"));
            Require(readback.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single().Text == "你好", "new format reader verifies the exact imported translation in the world");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ExplicitUnchangedSlotsKeepTheirAcceptedNoWriteCompletionContract()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var id = Guid.NewGuid();
        try
        {
            Start(work, world, id); using var exported = Success(Call(work, world, id, "export"));
            var reply = SessionExchangeJson.ReadReply(exported.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText());
            reply = reply with { Slots = reply.Slots.Select((slot, index) => slot with { State = index == 0 ? SessionReplyState.Translated : SessionReplyState.Unchanged, Text = index == 0 ? "translated" : null }).ToImmutableArray() };
            var file = Path.Combine(work, "mixed-reply.json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply)); using var submitted = Success(Call(work, world, id, "submit", "--reply", file));
            var service = new TranslationSessionService(Store(work)); var state = service.Read(id); var selected = state.Slots.Single(slot => slot.State == SessionSlotState.Translated);
            var unchanged = state.Slots.Where(slot => slot.State == SessionSlotState.Unchanged).ToDictionary(slot => Path.Combine(world, slot.Slot.Address.Artifact.RelativePath), slot => File.ReadAllBytes(Path.Combine(world, slot.Slot.Address.Artifact.RelativePath)));
            var inventory = new WorldInventory(world, state.Source.WorldId); var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(State(work), "cache")))
                .QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("explicit-translated-only", new(selected.Slot, selected.Reply!))]);
            var planFile = Path.Combine(work, "explicit-plan.json"); File.WriteAllText(planFile, PatchPlanJson.Write(plan));
            using var applied = Success(Apply(work, world, planFile)); using var completed = Success(Seal(work, world, id, state.Revision, Guid.NewGuid(), plan.PlanHash));
            var result = service.Read(id);
            Require(result.Slots.All(slot => slot.State == SessionSlotState.Applied) && result.Slots.Count(slot => slot.Reply is null) == unchanged.Count
                && unchanged.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "Unchanged means no requested translation: completion preserves null replies and does not invent world writes");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
