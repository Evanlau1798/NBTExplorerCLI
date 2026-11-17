using System.Text;
using System.Text.Json;
using System.Reflection;
using Next.Cli;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.CliScanTests;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class CliEditTests
{
    private const string Artifact = "data/custom.dat";
    private static readonly TypedPath Number = new([PathSegment.ForKey("data"), PathSegment.ForKey("number")]);
    private static string CreateWorld()
    {
        var root = WorldQueryTests.CreateWorld();
        Put(root, Artifact, BinaryNbt.WriteDocument(new("custom", new NbtCompound([new("DataVersion", new NbtInt(3953)),
            new("data", new NbtCompound([new("number", new NbtInt(1)), new("keep", new NbtLong(long.MaxValue))]))])))); return root;
    }
    private static string Value(int number) => TypedNbtJson.Write(new("", new NbtInt(number)));
    private static string Request(JsonElement address, int expected, int replacement)
    {
        using var edit = JsonDocument.Parse(TypedEditJson.Write(TypedEdit.Replace(Number, new NbtInt(expected), new NbtInt(replacement))));
        return JsonSerializer.Serialize(new { schemaFamily = "nbtcli.next.operations", schemaVersion = 1, data = new { operations = new[] { new { id = "edit-0", kind = "typedEdit", payload = new { address, edit = edit.RootElement } } } } });
    }
    private static int ReadNumber(string root) => ((NbtInt)NbtLocator.Resolve(BinaryNbt.ReadDocument(File.ReadAllBytes(Path.Combine(root, Artifact))).Document.Root, Number).Value!).Value;
    public static void PreviewApplyStatusAndRecoveryShareOneDurablePlan()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = File.ReadAllBytes(Path.Combine(root, Artifact)); var planFile = Path.Combine(work, "plan.json"); var state = Path.Combine(work, "state");
            using var preview = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), "--output", planFile, "--state-root", state));
            var plan = PatchPlanJson.Read(preview.RootElement.GetProperty("data").GetProperty("plan").GetRawText());
            Require(PatchPlanJson.Read(File.ReadAllText(planFile)).PlanHash == plan.PlanHash && plan.Operations.Single() is TypedPatch
                && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "set preview exports the immutable plan and writes no world bytes");
            Require(Run(work, "apply", root, "--plan", planFile, "--state-root", state).Exit == 2 && ReadNumber(root) == 1, "write requires an explicit offline declaration");
            using var applied = Success(Run(work, "apply", root, "--plan", planFile, "--state-root", state, "--world-offline"));
            var receipt = applied.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data");
            Require(receipt.GetProperty("success").GetBoolean() && receipt.GetProperty("requested").GetInt32() == 1 && ReadNumber(root) == 2, "public apply uses durable engine and exact readback");
            using var status = Success(Run(work, "status", root, "--plan-hash", plan.PlanHash.Hex, "--state-root", state));
            using var replay = Success(Run(work, "apply", root, "--plan", planFile, "--state-root", state, "--world-offline"));
            Require(status.RootElement.GetProperty("data").GetProperty("receipt").GetRawText() == replay.RootElement.GetProperty("data").GetProperty("receipt").GetRawText(), "status and repeated apply retain one persisted receipt version");
            var rollback = Run(work, "recover", root, "--plan-hash", plan.PlanHash.Hex, "--state-root", state, "--world-offline", "--rollback"); using var recovered = JsonDocument.Parse(rollback.Output);
            Require(rollback.Exit != 0 && ReadNumber(root) == 1 && recovered.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data").GetProperty("results")[0].GetProperty("outcome").GetString() == "rolledBack", "public recovery rolls the exact plan back with an honest non-success terminal receipt");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void BatchStdinAndSetCreateTheSameTypedIntent()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var selected = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number", "--types"));
            var address = selected.RootElement.GetProperty("data").GetProperty("source").GetProperty("address").Clone();
            var request = Request(address, 1, 3);
            var file = Path.Combine(work, "batch.json"); File.WriteAllText(file, request, new UTF8Encoding(false));
            using var direct = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(3), "--state-root", Path.Combine(work, "state")));
            using var batch = Success(RunInput(work, request, "batch", "-", "--world", root, "--state-root", Path.Combine(work, "state")));
            using var planned = Success(Run(work, "plan", root, "--request", file, "--state-root", Path.Combine(work, "state")));
            var first = PatchPlanJson.Read(direct.RootElement.GetProperty("data").GetProperty("plan").GetRawText()); var second = PatchPlanJson.Read(batch.RootElement.GetProperty("data").GetProperty("plan").GetRawText());
            Require(first.PlanHash == second.PlanHash && PatchPlanJson.Read(planned.RootElement.GetProperty("data").GetProperty("plan").GetRawText()).PlanHash == first.PlanHash && ReadNumber(root) == 1, "set, plan and batch stdin bind identical typed intent and preimages");
            using var applied = Success(Run(work, "batch", file, "--world", root, "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline"));
            Require(ReadNumber(root) == 3 && applied.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data").GetProperty("success").GetBoolean(), "batch file uses the same apply engine");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void VerifiedBackupRestoresCorruptAndMissingArtifacts()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var state = Path.Combine(work, "state"); var original = File.ReadAllBytes(Path.Combine(root, Artifact));
            using var backup = Success(Run(work, "backup", root, "--path", Artifact, "--state-root", state));
            var hash = backup.RootElement.GetProperty("data").GetProperty("manifestHash").GetString()!;
            foreach (var missing in new[] { false, true })
            {
                if (missing) File.Delete(Path.Combine(root, Artifact)); else File.WriteAllBytes(Path.Combine(root, Artifact), [0]);
                using var restored = Success(Run(work, "restore", root, "--manifest-hash", hash, "--path", Artifact, "--state-root", state, "--apply", "--world-offline"));
                Require(File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "verified raw restore accepts corrupt or missing current payload without parsing it");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void StalePlansAndInvalidWriteArgumentsPreserveWorldBytes()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var state = Path.Combine(work, "state"); var file = Path.Combine(work, "plan.json");
            using var preview = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), "--output", file, "--state-root", state));
            var path = Path.Combine(root, Artifact); var original = File.ReadAllBytes(path); File.WriteAllBytes(path, [0]);
            var stale = Run(work, "apply", root, "--plan", file, "--state-root", state, "--world-offline");
            Require(stale.Exit == 3 && File.ReadAllBytes(path).SequenceEqual(new byte[] { 0 }), "stale semantic plan refuses to overwrite changed source bytes");
            File.WriteAllBytes(path, original);
            var exportConflict = Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), "--apply", "--world-offline", "--output", file, "--state-root", state);
            Require(exportConflict.Exit == 3 && ReadNumber(root) == 1, "an existing result export conflicts before any world write");
            foreach (var args in new[] { new[] { "--apply" }, new[] { "--state-root", root }, new[] { "--value", "invalid" }, new[] { "--world-offline", "--world-offline" } })
                Require(Run(work, ["set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), .. args]).Exit != 0, "invalid or unsafe write arguments cannot bypass preview/preconditions");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "all rejected write requests preserve raw bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void PreconditionsWorldBindingAndInvalidHashStayExplicit()
    {
        var root = CreateWorld(); var other = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var state = Path.Combine(work, "state");
            using var selected = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number", "--types"));
            var address = selected.RootElement.GetProperty("data").GetProperty("source").GetProperty("address").Clone();
            var invalid = RunInput(work, Request(address, 999, 2), "batch", "-", "--world", root, "--apply", "--world-offline", "--state-root", state); using var rejected = JsonDocument.Parse(invalid.Output);
            Require(invalid.Exit == 3 && rejected.RootElement.GetProperty("error").GetProperty("code").GetString() == "CONFLICT" && ReadNumber(root) == 1,
                "a failed typed precondition must be Conflict, not an internal fault: " + invalid.Output);
            var valueFile = Path.Combine(work, "value.json"); File.WriteAllText(valueFile, Value(2)); var planFile = Path.Combine(work, "plan.json");
            using var preview = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value-file", valueFile, "--output", planFile, "--state-root", state));
            Require(Run(work, "apply", other, "--plan", planFile, "--state-root", state, "--world-offline").Exit == 3 && ReadNumber(other) == 1, "byte-identical other roots cannot consume the original plan authority");
            Require(Run(work, "status", root, "--plan-hash", "invalid", "--state-root", state).Exit == 2, "invalid hash arguments fail as arguments");
            Require(Run(work, "status", root, "--plan-hash", new string('0', 64), "--state-root", state).Exit == 5, "a missing receipt is explicit, never empty success");
        }
        finally { Directory.Delete(root, true); Directory.Delete(other, true); Directory.Delete(work, true); }
    }
    public static void MultipleTypedOperationsAccountOnceAndRejectedPlansSkipEveryIntent()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var state = Path.Combine(work, "state");
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var source = SourceAddressJson.Read(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address").GetRawText());
            string Batch(long expected)
            {
                using var first = JsonDocument.Parse(Request(JsonDocument.Parse(SourceAddressJson.Write(source)).RootElement.Clone(), 1, 4));
                var keep = new TypedPath([PathSegment.ForKey("data"), PathSegment.ForKey("keep")]);
                using var address = JsonDocument.Parse(SourceAddressJson.Write(new(source.Artifact, keep, "query.node")));
                using var edit = JsonDocument.Parse(TypedEditJson.Write(TypedEdit.Replace(keep, new NbtLong(expected), new NbtLong(long.MinValue))));
                using var second = JsonDocument.Parse(JsonSerializer.Serialize(new { id = "edit-1", kind = "typedEdit", payload = new { address = address.RootElement, edit = edit.RootElement } }));
                return JsonSerializer.Serialize(new { schemaFamily = "nbtcli.next.operations", schemaVersion = 1, data = new { operations = new[] { first.RootElement.GetProperty("data").GetProperty("operations")[0], second.RootElement } } });
            }
            var rejected = RunInput(work, Batch(42), "batch", "-", "--world", root, "--state-root", state, "--apply", "--world-offline"); using var failure = JsonDocument.Parse(rejected.Output);
            var data = failure.RootElement.GetProperty("data");
            Require(rejected.Exit == 3 && data.ValueKind == JsonValueKind.Object && data.GetProperty("validation").GetProperty("requested").GetInt32() == 2
                && data.GetProperty("validation").GetProperty("results").EnumerateArray().All(result => result.GetProperty("outcome").GetString() == "skipped") && ReadNumber(root) == 1,
                "pre-plan rejection must account for all valid intents without pretending any world write happened");
            using var applied = Success(RunInput(work, Batch(long.MaxValue), "batch", "-", "--world", root, "--state-root", state, "--apply", "--world-offline"));
            var receipt = applied.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data"); var counts = receipt.GetProperty("counts");
            var document = BinaryNbt.ReadDocument(File.ReadAllBytes(Path.Combine(root, Artifact))).Document;
            Require(receipt.GetProperty("requested").GetInt32() == 2 && receipt.GetProperty("results").GetArrayLength() == 2 && counts.GetArrayLength() == 9
                && counts.EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()) == 2 && ReadNumber(root) == 4
                && ((NbtLong)NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("data"), PathSegment.ForKey("keep")])).Value!).Value == long.MinValue,
                "two typed operations sharing one output retain long width and each account exactly once");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void UnreadableIntentSourcesKeepTheirActualReadFailure()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var request = Request(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address"), 1, 2);
            using (var locked = File.Open(Path.Combine(root, Artifact), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var result = RunInput(work, request, "batch", "-", "--world", root, "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline");
                using var error = JsonDocument.Parse(result.Output); var code = error.RootElement.GetProperty("error").GetProperty("code").GetString();
                Require(result.Exit == 5 && code == "READ_ERROR" && error.RootElement.GetProperty("data").GetProperty("validation").GetProperty("requested").GetInt32() == 1,
                    $"unreadable source must not become a typed precondition conflict; exit={result.Exit}, code={code}");
            }
            Require(ReadNumber(root) == 1, "failed reads do not alter the typed source");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void PartialApplyAndRecoveryPreserveEveryOperationResult()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            const string other = "data/zzz.dat"; Put(root, other, File.ReadAllBytes(Path.Combine(root, Artifact))); var state = Path.Combine(work, "state");
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var source = SourceAddressJson.Read(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address").GetRawText());
            using var sourceJson = JsonDocument.Parse(SourceAddressJson.Write(source)); using var first = JsonDocument.Parse(Request(sourceJson.RootElement, 1, 4));
            using var address = JsonDocument.Parse(SourceAddressJson.Write(new(new(source.Artifact.WorldId, other, ArtifactKind.Nbt), Number, "query.node")));
            using var edit = JsonDocument.Parse(TypedEditJson.Write(TypedEdit.Replace(Number, new NbtInt(1), new NbtInt(9))));
            using var second = JsonDocument.Parse(JsonSerializer.Serialize(new { id = "edit-1", kind = "typedEdit", payload = new { address = address.RootElement, edit = edit.RootElement } }));
            var request = JsonSerializer.Serialize(new { schemaFamily = "nbtcli.next.operations", schemaVersion = 1, data = new { operations = new[] { first.RootElement.GetProperty("data").GetProperty("operations")[0], second.RootElement } } });
            string hash;
            using (var reader = File.Open(Path.Combine(root, other), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = RunInput(work, request, "batch", "-", "--world", root, "--state-root", state, "--apply", "--world-offline"); using var partial = JsonDocument.Parse(result.Output);
                var receipt = partial.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data"); hash = receipt.GetProperty("planHash").GetString()!;
                Require(result.Exit == 7 && !partial.RootElement.GetProperty("success").GetBoolean() && receipt.GetProperty("requested").GetInt32() == 2
                    && receipt.GetProperty("results").GetArrayLength() == 2 && receipt.GetProperty("counts").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()) == 2
                    && ReadNumber(root) == 4, "a real second replacement failure retains partial commit and all operation outcomes with nonzero RecoveryRequired");
            }
            using var recovered = Success(Run(work, "recover", root, "--plan-hash", hash, "--state-root", state, "--world-offline"));
            var final = recovered.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data");
            var document = BinaryNbt.ReadDocument(File.ReadAllBytes(Path.Combine(root, other))).Document;
            Require(ReadNumber(root) == 4 && ((NbtInt)NbtLocator.Resolve(document.Root, Number).Value!).Value == 9
                && final.GetProperty("requested").GetInt32() == 2 && final.GetProperty("counts").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()) == 2,
                "public recovery completes the persisted plan without counting an earlier applied member twice");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void PreparationFailureRetainsIntentAccountingAndWorldBytes()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var request = Request(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address"), 1, 2);
            using (var lease = File.Open(Path.Combine(root, ".nbtcli-writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var result = RunInput(work, request, "batch", "-", "--world", root, "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline");
                using var error = JsonDocument.Parse(result.Output); var data = error.RootElement.GetProperty("data");
                Require(result.Exit != 0 && data.ValueKind == JsonValueKind.Object && data.GetProperty("validation").GetProperty("phase").GetString() == "PreparationRejected"
                    && data.GetProperty("validation").GetProperty("requested").GetInt32() == 1 && ReadNumber(root) == 1,
                    "an actual writer lease failure in preparation must retain skipped intents and write no source bytes");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void BulkBackupRetainsCatalogResourceLimits()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = Path.Combine(root, "data", "huge.dat"); using (var output = File.Create(file)) output.SetLength(67_108_865);
            var result = Run(work, "backup", root, "--state-root", Path.Combine(work, "state")); using var error = JsonDocument.Parse(result.Output);
            Require(result.Exit == 6 && error.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT",
                "bulk raw backup must preserve the physical catalog's explicit resource limit: " + result.Output);
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void InvalidOperationContractsFailBeforeCreatingRepositories()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var request = Request(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address"), 1, 2);
            using var parsed = JsonDocument.Parse(request); var operation = parsed.RootElement.GetProperty("data").GetProperty("operations")[0].GetRawText();
            var inputs = new[] { "{}", request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal), request.Replace("typedEdit", "unknownIntent", StringComparison.Ordinal),
                "{\"schemaFamily\":\"nbtcli.next.operations\",\"schemaVersion\":1,\"data\":{\"operations\":[" + operation + "," + operation + "]}}",
                request.Replace("\"operations\":", "\"extra\":0,\"operations\":", StringComparison.Ordinal) };
            for (var index = 0; index < inputs.Length; index++)
            {
                var state = Path.Combine(work, "invalid-" + index); var result = RunInput(work, inputs[index], "batch", "-", "--world", root, "--state-root", state, "--apply", "--world-offline");
                Require(result.Exit == (index == 1 ? 4 : 5) && !Directory.Exists(state) && ReadNumber(root) == 1, "malformed, duplicate and unsupported operation contracts fail before repository/world writes");
            }
            var value = Path.Combine(work, "invalid-utf8.json"); File.WriteAllBytes(value, [255]); var typedState = Path.Combine(work, "typed-state");
            Require(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value-file", value, "--state-root", typedState).Exit == 5 && !Directory.Exists(typedState), "invalid UTF8 value files fail before creating state");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void TypedRegionEditBindsDimensionChunkAndPreservesItsNeighbor()
    {
        var root = WorldReferenceTests.CreateWorld(true); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = Path.Combine(root, WorldReferenceTests.Entities); var before = File.ReadAllBytes(file); var original = RegionFile.Read(before); var neighbor = original.Chunks[0];
            using var result = Success(Run(work, "set", root, "--path", WorldReferenceTests.Entities, "--chunk", "1,0", "--dimension", "minecraft:overworld", "--pointer", "/Entities/0/UUID/0", "--value", Value(99), "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline"));
            var after = File.ReadAllBytes(file); var changed = RegionFile.Read(after);
            var uuid = NbtLocator.Resolve(changed.ReadChunk(1).Document.Root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("UUID"), PathSegment.ForIndex(0)]));
            Require(uuid.Value == new NbtInt(99) && changed.Chunks[0] == neighbor && before.AsSpan(neighbor.SectorOffset * 4096, neighbor.SectorCount * 4096).SequenceEqual(after.AsSpan(neighbor.SectorOffset * 4096, neighbor.SectorCount * 4096)),
                "typed array edit targets the declared global chunk while preserving the unselected physical allocation and timestamp");
            var rejected = Run(work, "set", root, "--path", WorldReferenceTests.Entities, "--chunk", "1,0", "--dimension", "minecraft:the_nether", "--pointer", "/Entities/0/UUID/0", "--value", Value(100), "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline");
            Require(rejected.Exit == 3 && File.ReadAllBytes(file).SequenceEqual(after), "an incorrect dimension cannot authorize a typed region write");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void OversizedDurableReceiptIsRejectedBeforeTheFirstWrite()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var request = Request(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address"), 1, 2)
                .Replace("\"edit-0\"", "\"" + new string('\u00e9', 6_000_000) + "\"", StringComparison.Ordinal);
            var file = Path.Combine(work, "request.json"); File.WriteAllText(file, request, new UTF8Encoding(false)); var state = Path.Combine(work, "state");
            var result = Run(work, "batch", file, "--world", root, "--state-root", state, "--apply", "--world-offline"); using var envelope = JsonDocument.Parse(result.Output);
            var code = envelope.RootElement.GetProperty("error").GetProperty("code").GetString(); var number = ReadNumber(root); var status = -1; var recovery = -1;
            if (number != 1)
            {
                var journal = Directory.GetFiles(Path.Combine(state, "journals"), "*.json").Select(File.ReadAllText)
                    .First(text => text.Contains(ApplyJournal.SchemaFamily, StringComparison.Ordinal));
                var record = ApplyJournalJson.Read(journal);
                status = Run(work, "status", root, "--state-root", state, "--plan-hash", record.Binding.PlanHash.Hex).Exit;
                recovery = Run(work, "recover", root, "--state-root", state, "--plan-hash", record.Binding.PlanHash.Hex, "--world-offline").Exit;
            }
            Require(result.Exit == 6 && code == "RESOURCE_LIMIT" && number == 1,
                $"receipt expansion must be refused before commit; exit={result.Exit}, code={code}, numberAfter={number}, statusExit={status}, recoverExit={recovery}");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void CompletedRollbacksUseTheJournalDirectionForMixedAndNoopPlans()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            const string other = "data/zzz.dat"; Put(root, other, File.ReadAllBytes(Path.Combine(root, Artifact))); var state = Path.Combine(work, "state");
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var address = get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address");
            using var first = JsonDocument.Parse(Request(address, 1, 2)); var source = SourceAddressJson.Read(address.GetRawText());
            using var otherAddress = JsonDocument.Parse(SourceAddressJson.Write(new(new(source.Artifact.WorldId, other, ArtifactKind.Nbt), Number, "query.node")));
            using var edit = JsonDocument.Parse(TypedEditJson.Write(TypedEdit.Replace(Number, new NbtInt(1), new NbtInt(1))));
            using var second = JsonDocument.Parse(JsonSerializer.Serialize(new { id = "edit-1", kind = "typedEdit", payload = new { address = otherAddress.RootElement, edit = edit.RootElement } }));
            var request = JsonSerializer.Serialize(new { schemaFamily = "nbtcli.next.operations", schemaVersion = 1, data = new { operations = new[] { first.RootElement.GetProperty("data").GetProperty("operations")[0], second.RootElement } } });
            using var applied = Success(RunInput(work, request, "batch", "-", "--world", root, "--state-root", state, "--apply", "--world-offline"));
            var hash = applied.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data").GetProperty("planHash").GetString()!;
            var mixed = Run(work, "recover", root, "--state-root", state, "--plan-hash", hash, "--rollback", "--world-offline"); using var mixedJson = JsonDocument.Parse(mixed.Output);
            using var noop = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(1), "--operation-id", "noop-only", "--state-root", state, "--apply", "--world-offline"));
            var noopHash = noop.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data").GetProperty("planHash").GetString()!;
            var unchanged = Run(work, "recover", root, "--state-root", state, "--plan-hash", noopHash, "--rollback", "--world-offline"); using var noopJson = JsonDocument.Parse(unchanged.Output);
            Require(mixed.Exit == 8 && unchanged.Exit == 8 && !mixedJson.RootElement.GetProperty("success").GetBoolean() && !noopJson.RootElement.GetProperty("success").GetBoolean()
                && mixedJson.RootElement.GetProperty("data").GetProperty("direction").GetString() == "rollback" && noopJson.RootElement.GetProperty("data").GetProperty("direction").GetString() == "rollback" && ReadNumber(root) == 1,
                $"terminal rollback must not depend on the mixture of image outcomes; mixedExit={mixed.Exit}, noopExit={unchanged.Exit}");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void ApplyAndRestoreReadFailuresKeepTheirParsedIntentAccounting()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild(); var failures = new List<string>();
        try
        {
            var state = Path.Combine(work, "state"); var file = Path.Combine(work, "plan.json");
            using var preview = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), "--state-root", state, "--output", file));
            using var backup = Success(Run(work, "backup", root, "--path", Artifact, "--state-root", state)); var manifest = backup.RootElement.GetProperty("data").GetProperty("manifestHash").GetString()!;
            using (var locked = File.Open(Path.Combine(root, Artifact), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                foreach (var args in new[] { new[] { "apply", root, "--plan", file }, new[] { "restore", root, "--manifest-hash", manifest, "--apply" } })
                {
                    var result = Run(work, [.. args, "--state-root", state, "--world-offline"]); using var json = JsonDocument.Parse(result.Output); var data = json.RootElement.GetProperty("data");
                    if (result.Exit != 5 || data.ValueKind != JsonValueKind.Object || data.GetProperty("validation").GetProperty("phase").GetString() != "PlanRejected"
                        || data.GetProperty("validation").GetProperty("requested").GetInt32() != 1) failures.Add(args[0] + ":exit=" + result.Exit + ", data=" + data.ValueKind);
                }
            Require(failures.Count == 0 && ReadNumber(root) == 1, "read failures after parsing valid intents retain validation records: " + string.Join(" | ", failures));
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void InvalidDataDuringPreparationRetainsValidationResults()
    {
        foreach (var error in new Exception[] { new InvalidDataException("Invalid persisted candidate"), new AggregateException(new IOException("Candidate cleanup failed")) })
            AssertPreparationRejected(error, error is InvalidDataException ? "INVALID_DATA" : "INTERNAL_ERROR");
    }
    private static void AssertPreparationRejected(Exception preparationError, string expectedCode)
    {
        var assembly = typeof(CliApplication).Assembly; var method = assembly.GetType("Next.Cli.WriteCommands")!.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static)!;
        Exception? failure = null;
        try { _ = method.Invoke(null, [new PatchOperation[] { PlanTests.Edit() }, (Func<PreparedWorldPlan>)(() => throw preparationError)]); }
        catch (TargetInvocationException error) { failure = error.InnerException; }
        var fault = failure ?? throw new InvalidOperationException("Controlled invalid-data fault was not invoked");
        var code = (string)typeof(CliApplication).GetMethod("Code", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [fault])!;
        var payload = fault.GetType().GetProperty("Result", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(fault);
        using var envelope = JsonDocument.Parse(CommandOutputJson.Write("batch", payload, "Failed", code)); var data = envelope.RootElement.GetProperty("data");
        Require(code == expectedCode && data.ValueKind == JsonValueKind.Object && data.GetProperty("validation").GetProperty("phase").GetString() == "PreparationRejected"
            && data.GetProperty("validation").GetProperty("requested").GetInt32() == 1, "controlled pre-commit " + preparationError.GetType().Name + " keeps parsed intentions; code=" + code);
    }
    public static void RejectedLargeIdsStillProduceABoundedMachineError()
    {
        var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
            var request = Request(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address"), 1, 2)
                .Replace("\"edit-0\"", "\"" + new string('\u00e9', 12_000_000) + "\"", StringComparison.Ordinal);
            var file = Path.Combine(work, "request.json"); File.WriteAllText(file, request, new UTF8Encoding(false));
            var result = Run(work, "batch", file, "--world", root, "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline");
            Require(result.Exit == 6 && result.Output.StartsWith('{') && ReadNumber(root) == 1,
                $"a rejection which cannot echo all IDs must fail admission with ResourceLimit JSON, not an unhandled error; exit={result.Exit}, outputLength={result.Output.Length}");
            using var error = JsonDocument.Parse(result.Output); Require(error.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT", "bounded rejection retains the resource classification");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void RestorePlanBackupRejectionsAccountForEveryValidIntent()
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "missing", "corrupt", "mixed" })
        {
            var root = CreateWorld(); var work = ScratchDirectory.CreateChild();
            try
            {
                const string other = "data/zzz.dat"; Put(root, other, File.ReadAllBytes(Path.Combine(root, Artifact))); var state = Path.Combine(work, "state");
                using var get = Success(Run(work, "get", root, "--path", Artifact, "--pointer", "/data/number"));
                var address = SourceAddressJson.Read(get.RootElement.GetProperty("data").GetProperty("source").GetProperty("address").GetRawText());
                var inventory = new WorldInventory(root, address.Artifact.WorldId); var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
                var artifacts = new[] { address.Artifact, new ArtifactAddress(inventory.WorldId, other, ArtifactKind.Nbt) };
                var backup = new WorldBackupService(inventory, objects).Create(artifacts.Select(artifact => new ArtifactPrecondition(artifact, ArtifactImage.Present(Hash256.Compute(inventory.Read(artifact))))));
                using var edit = Success(Run(work, "set", root, "--path", Artifact, "--pointer", "/data/number", "--value", Value(2), "--apply", "--world-offline", "--state-root", Path.Combine(work, "edit-state")));
                var operations = artifacts.Select((artifact, index) => new RestorePatch("restore-" + index, new ArtifactRestore(backup.ManifestHash,
                    [backup.Manifest.Artifacts.Single(item => item.Artifact == artifact)], [new(artifact, ArtifactImage.Present(Hash256.Compute(inventory.Read(artifact))))]))).ToArray();
                var plan = new WorldPlanBuilder(inventory).Build(backup, operations);
                if (scenario == "mixed")
                {
                    var last = operations[1].Restore;
                    operations[1] = new("restore-1", new ArtifactRestore(Hash256.Compute([99]), last.AuthorizedArtifacts, last.CurrentImages));
                    plan = new(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, plan.PolicyVersion, operations, plan.Preconditions, plan.Outputs, plan.Dependencies, plan.Recovery);
                }
                else
                {
                    var manifest = Path.Combine(state, "objects", "manifest-" + backup.ManifestHash.Hex + ".json");
                    if (scenario == "missing") File.Delete(manifest); else File.WriteAllBytes(manifest, [0]);
                }
                var file = Path.Combine(work, "plan.json"); File.WriteAllText(file, PatchPlanJson.Write(plan), new UTF8Encoding(false));
                var result = Run(work, "apply", root, "--plan", file, "--state-root", state, "--world-offline"); using var output = JsonDocument.Parse(result.Output);
                var data = output.RootElement.GetProperty("data"); var expectedCode = scenario == "missing" ? "FILE_NOT_FOUND" : scenario == "corrupt" ? "INVALID_DATA" : "UNSUPPORTED_SOURCE";
                var valid = result.Exit == (scenario == "mixed" ? 4 : 5) && output.RootElement.GetProperty("error").GetProperty("code").GetString() == expectedCode
                    && data.ValueKind == JsonValueKind.Object && data.GetProperty("validation").GetProperty("phase").GetString() == "PlanRejected";
                if (valid)
                {
                    var validation = data.GetProperty("validation"); var results = validation.GetProperty("results").EnumerateArray().ToArray();
                    valid = validation.GetProperty("requested").GetInt32() == 2 && results.Select(item => item.GetProperty("operationId").GetString()).SequenceEqual(new[] { "restore-0", "restore-1" })
                        && results.All(item => item.GetProperty("outcome").GetString() == "skipped") && validation.GetProperty("counts").GetArrayLength() == 9
                        && validation.GetProperty("counts").EnumerateArray().All(item => item.GetProperty("count").GetInt32() == (item.GetProperty("outcome").GetString() == "skipped" ? 2 : 0));
                }
                if (!valid) failures.Add(scenario + ":exit=" + result.Exit + ", data=" + data.ValueKind);
                Require(ReadNumber(root) == 2 && File.ReadAllBytes(Path.Combine(root, other)).SequenceEqual(backup.ReadArtifact(artifacts[1])!), "restore rejection does not replace either current world artifact");
            }
            finally { Directory.Delete(root, true); Directory.Delete(work, true); }
        }
        Require(failures.Count == 0, "valid restore plans retain complete pre-commit rejection accounting: " + string.Join(" | ", failures));
    }
}
