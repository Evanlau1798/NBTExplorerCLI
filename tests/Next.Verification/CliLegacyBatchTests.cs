using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CliScanTests;

namespace Next.Verification;

internal static class CliLegacyBatchTests
{
    private const string Artifact = "data/legacy.dat";
    private static string WorldRoot()
    {
        var root = WorldQueryTests.CreateWorld();
        Put(root, Artifact, BinaryNbt.WriteDocument(new("kept-root", new NbtCompound([new("DataVersion", new NbtInt(3953)),
            new("data", new NbtCompound([new("message", new NbtString("Before")), new("number", new NbtInt(7)), new("keep", new NbtLong(long.MaxValue))]))]))));
        return root;
    }
    private static string Input(string root, object[] operations, object? options = null) => JsonSerializer.Serialize(new { WorldPath = root, Operations = operations, Options = options });
    private static NbtDocument Read(string root) => BinaryNbt.ReadDocument(File.ReadAllBytes(Path.Combine(root, Artifact))).Document;
    private static string[] Args(string root, string work, string report, params string[] flags) => ["batch", "-", "--legacy", "--world", root,
        "--migration-report", report, "--state-root", Path.Combine(work, "state"), .. flags];
    public static void ExplicitLegacyPreviewRetainsBytesAndUsesTheNormalWriter()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var raw = Input(root, [new { Id = "old-id", Path = Artifact + "#data/message", Value = "{\"text\":\"Literal, not a typed wrapper\"}", LineIndex = 12 }],
                new { CreateBackup = false, ValidateBeforeWrite = false, Future = "retain" });
            var report = Path.Combine(work, "report.json"); var planFile = Path.Combine(work, "plan.json"); var before = File.ReadAllBytes(Path.Combine(root, Artifact));
            Require(RunInput(work, raw, "batch", "-", "--world", root).Exit != 0, "legacy input is never guessed in the default versioned workflow");
            using var preview = Success(RunInput(work, raw, Args(root, work, report, "--output", planFile)));
            var plan = PatchPlanJson.Read(File.ReadAllText(planFile));
            Require(plan.Operations.Single() is TypedPatch && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(before), "legacy preview resolves explicit typed intent and never writes the world");
            using var migration = JsonDocument.Parse(File.ReadAllText(report)); var data = migration.RootElement.GetProperty("data");
            Require(Convert.FromBase64String(data.GetProperty("rawInputBase64").GetString()!).SequenceEqual(Encoding.UTF8.GetBytes(raw))
                && data.GetProperty("items")[0].GetProperty("legacyId").GetString() == "old-id"
                && data.GetRawText().Contains("LegacyFieldUnused", StringComparison.Ordinal)
                && data.GetRawText().Contains("LegacyUnknownField", StringComparison.Ordinal), "report preserves exact input, provenance and unused/unknown preferences");
            using var applied = Success(Run(work, "apply", root, "--plan", planFile, "--world-offline", "--state-root", Path.Combine(work, "state")));
            Require(NbtLocator.Resolve(Read(root).Root, new([PathSegment.ForKey("data"), PathSegment.ForKey("message")])).Value == new NbtString("{\"text\":\"Literal, not a typed wrapper\"}")
                && NbtLocator.Resolve(Read(root).Root, new([PathSegment.ForKey("data"), PathSegment.ForKey("keep")])).Value == new NbtLong(long.MaxValue), "string values retain their original text and unselected types/values stay intact");
            var receipt = applied.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("data");
            Require(receipt.GetProperty("success").GetBoolean() && receipt.GetProperty("requested").GetInt32() == 1, "normal durable receipt accounts for the mapped operation");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void TypedValuesApplyWithSafeDefaultsAndOldIdsRemainProvenance()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var raw = Input(root, [new { Id = "duplicate", Path = Artifact + "#data/number", Value = TypedNbtJson.Write(new("", new NbtInt(9))) },
                new { Id = "duplicate", Path = Artifact + "#data/message", Value = "After" }]);
            var report = Path.Combine(work, "typed-report.json");
            Require(RunInput(work, raw, Args(root, work, report, "--apply")).Exit == 2 && !File.Exists(report), "offline declaration is required before import side effects for a write");
            using var applied = Success(RunInput(work, raw, Args(root, work, report, "--apply", "--world-offline")));
            var receipt = applied.RootElement.GetProperty("data").GetProperty("result").GetProperty("receipt").GetProperty("data");
            Require(receipt.GetProperty("requested").GetInt32() == 2 && receipt.GetProperty("results").EnumerateArray().Select(row => row.GetProperty("operationId").GetString()).Distinct().Count() == 2
                && NbtLocator.Resolve(Read(root).Root, new([PathSegment.ForKey("data"), PathSegment.ForKey("number")])).Value == new NbtInt(9), "explicit versioned values retain their requested type; duplicate old IDs cannot become authority");
            Require(applied.RootElement.GetProperty("data").TryGetProperty("convenienceBackupHash", out var backup) && backup.ValueKind == JsonValueKind.String,
                "default CreateBackup produces an independently addressable verified full-world convenience backup");
            using var migration = JsonDocument.Parse(File.ReadAllText(report));
            Require(migration.RootElement.GetProperty("data").GetProperty("createBackup").GetBoolean(), "missing options retain the documented safe backup preference");
            Require(Directory.EnumerateFiles(Path.Combine(work, "state", "objects"), "*", SearchOption.AllDirectories).Any(), "legacy preferences never bypass the engine's verified recovery material");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void InvalidOrAmbiguousMappingsAndReportFailuresNeverWriteWorlds()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = File.ReadAllBytes(Path.Combine(root, Artifact));
            foreach (var mode in new[] { "plain-number", "world", "empty", "case-duplicate", "unknown-target" })
            {
                var raw = mode switch
                {
                    "world" => Input(work, [new { Path = Artifact + "#data/message", Value = "After" }]),
                    "empty" => Input(root, []),
                    "case-duplicate" => "{\"WorldPath\":" + JsonSerializer.Serialize(root) + ",\"worldpath\":" + JsonSerializer.Serialize(root) + ",\"Operations\":[]}",
                    _ => Input(root, [new { Path = Artifact + (mode == "plain-number" ? "#data/number" : "#data/missing"), Value = "9" }])
                };
                var report = Path.Combine(work, mode + ".json"); var result = RunInput(work, raw, Args(root, work, report, "--apply", "--world-offline"));
                Require(result.Exit != 0 && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original) && File.Exists(report), "invalid legacy mapping retains raw report without world writes: " + mode);
            }
            var valid = Input(root, [new { Path = Artifact + "#data/message", Value = "After" }]); var occupied = Path.Combine(work, "occupied.json"); File.WriteAllText(occupied, "Keep");
            Require(RunInput(work, valid, Args(root, work, occupied, "--apply", "--world-offline")).Exit == 3 && File.ReadAllText(occupied) == "Keep"
                && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "an existing report refuses before any world write");
            Require(RunInput(work, valid, Args(root, work, Path.Combine(root, "unsafe.json"), "--apply", "--world-offline")).Exit != 0
                && !File.Exists(Path.Combine(root, "unsafe.json")), "migration reports cannot be written inside the selected world");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void RegionAndWindowsPathsBindExactChunksAndRejectAmbiguity()
    {
        var root = WorldReferenceTests.CreateWorld(duplicate: true); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities)));
            var raw = JsonSerializer.Serialize(new { worldpath = root, operations = new[] { new { id = "old", path = "entities\\r.0.0.mca\\[0,0]\\Entities\\[0]\\Tags\\[0]", value = "Updated" } } });
            var file = Path.Combine(work, "request.json"); File.WriteAllText(file, raw, new UTF8Encoding(false));
            using var applied = Success(Run(work, "batch", file, "--legacy", "--world", root, "--migration-report", Path.Combine(work, "region.json"), "--state-root", Path.Combine(work, "state"), "--apply", "--world-offline"));
            var current = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities)));
            var path = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("Tags"), PathSegment.ForIndex(0)]);
            Require(NbtLocator.Resolve(current.ReadChunk(0).Document.Root, path).Value == new NbtString("Updated")
                && current.Chunks[1] == original.Chunks[1] && current.CompressedPayload(1).SequenceEqual(original.CompressedPayload(1)), "case-insensitive old fields and Windows separators bind one exact chunk and preserve the unselected chunk");
            var before = File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities));
            var ambiguous = Input(root, [new { Path = "r.0.0.mca/Chunk[0,0]/Entities[0]/Tags[0]", Value = "Wrong" }]);
            Require(RunInput(work, ambiguous, Args(root, work, Path.Combine(work, "ambiguous.json"), "--apply", "--world-offline")).Exit == 3
                && File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities)).SequenceEqual(before), "a bare basename shared by terrain/entities cannot select the first match");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void NativeReadFailuresAndInvalidTypedValuesKeepTheirErrorClassification()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = File.ReadAllBytes(Path.Combine(root, Artifact)); var report = Path.Combine(work, "locked.json"); Invocation result;
            using (var locked = new FileStream(Path.Combine(root, Artifact), FileMode.Open, FileAccess.Read, FileShare.None))
                result = RunInput(work, Input(root, [new { Path = Artifact + "#data/message", Value = "After" }]), Args(root, work, report, "--apply", "--world-offline"));
            Require(result.Exit == 5 && File.Exists(report) && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "native read/locking failure must remain an I/O-class exit with raw input retained, actual=" + result.Exit);
            var invalid = Input(root, [new { Path = Artifact + "#data/number", Value = "{\"schemaFamily\":\"nbtcli.next.typed-nbt\"}" }]);
            result = RunInput(work, invalid, Args(root, work, Path.Combine(work, "malformed.json"), "--apply", "--world-offline"));
            Require(result.Exit == 5 && result.Output.Contains("MALFORMED_SOURCE", StringComparison.Ordinal) && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "an invalid explicit typed document keeps the shared malformed-input classification");
            result = RunInput(work, Input(root, [new { Path = Artifact + "#data/number", Value = "{\"schemaFamily\":1}" }]), Args(root, work, Path.Combine(work, "wrong-family-type.json")));
            Require(result.Exit == 4 && File.Exists(Path.Combine(work, "wrong-family-type.json")), "an untyped JSON wrapper never becomes an internal error or loses its migration report");
            foreach (var family in new[] { "\\ud800", "\\udfff", "\\udfff\\ud800" })
            {
                var raw = Input(root, [new { Path = Artifact + "#data/number", Value = "{\"schemaFamily\":\"" + family + "\"}" }]);
                var invalidReport = Path.Combine(work, "family-" + family.Length + family[5] + ".json");
                result = RunInput(work, raw, Args(root, work, invalidReport));
                Require(result.Exit == 4 && File.Exists(invalidReport) && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "escaped non-scalar family names are unsupported and retain the migration report, actual=" + result.Exit);
                using var saved = JsonDocument.Parse(File.ReadAllText(invalidReport));
                Require(Convert.FromBase64String(saved.RootElement.GetProperty("data").GetProperty("rawInputBase64").GetString()!).SequenceEqual(Encoding.UTF8.GetBytes(raw)), "rejected Unicode family values retain exact original input bytes");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void LegacySeparatorsCannotChooseBetweenTwoDifferentTypedPaths()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var data = new NbtCompound([new("branch/leaf", new NbtCompound([new("value", new NbtString("Literal"))])),
                new("branch", new NbtCompound([new("leaf", new NbtCompound([new("value", new NbtString("Nested"))]))]))]);
            Put(root, Artifact, BinaryNbt.WriteDocument(new("", new NbtCompound([new("DataVersion", new NbtInt(3953)), new("data", data)]))));
            var before = File.ReadAllBytes(Path.Combine(root, Artifact)); var raw = Input(root, [new { Path = Artifact + "#data/branch/leaf/value", Value = "Changed" }]);
            var result = RunInput(work, raw, Args(root, work, Path.Combine(work, "ambiguous-key.json"), "--apply", "--world-offline"));
            if (result.Exit == 0)
                Require(NbtLocator.Resolve(Read(root).Root, new([PathSegment.ForKey("data"), PathSegment.ForKey("branch"), PathSegment.ForKey("leaf"), PathSegment.ForKey("value")])).Value == new NbtString("Changed"),
                    "adverse control: the adapter resolved and wrote one of two distinct possible key paths");
            Require(result.Exit == 3 && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(before), "a partial literal key containing legacy separators is ambiguous even when the entire remaining path is not a key");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
    public static void InputLimitsReportIoAndDisabledConvenienceBackupKeepRecoverySafe()
    {
        var root = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = File.ReadAllBytes(Path.Combine(root, Artifact)); var raw = Input(root, [new { Path = Artifact + "#data/message", Value = "After" }], new { CreateBackup = false, ValidateBeforeWrite = false });
            var huge = Path.Combine(work, "huge.json"); File.WriteAllBytes(huge, new byte[LegacyBatchJson.MaximumInputBytes + 1]); var report = Path.Combine(work, "bounded.json");
            Require(Run(work, "batch", huge, "--legacy", "--world", root, "--migration-report", report, "--apply", "--world-offline", "--state-root", Path.Combine(work, "state")).Exit == 6
                && !File.Exists(report) && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "oversized raw input is refused before retention or world writes");
            Require(RunInput(work, raw, Args(root, work, report, "--output", report)).Exit == 2 && !File.Exists(report), "report and plan output cannot claim the same new destination");
            var decodedLimit = JsonSerializer.Serialize(new { WorldPath = root, Operations = new[] { new { Path = Artifact + "#data/message", Value = "After" } }, Unused = new string('a', 8_388_608) });
            var limitedReport = Path.Combine(work, "decoded-limit.json");
            Require(RunInput(work, decodedLimit, Args(root, work, limitedReport)).Exit == 6 && File.Exists(limitedReport)
                && File.ReadAllBytes(Path.Combine(root, Artifact)).SequenceEqual(original), "input within the raw bound retains a report even when decoded JSON exceeds its resource budget");
            var inputFile = Path.Combine(work, "raw.json"); File.WriteAllBytes(inputFile, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(raw)]);
            using var applied = Success(Run(work, "batch", inputFile, "--legacy", "--world", root, "--migration-report", report, "--apply", "--world-offline", "--state-root", Path.Combine(work, "state")));
            Require(applied.RootElement.GetProperty("data").GetProperty("convenienceBackupHash").ValueKind == JsonValueKind.Null, "false disables the extra convenience copy");
            var manifestHash = applied.RootElement.GetProperty("data").GetProperty("result").GetProperty("backupManifestHash");
            Require(manifestHash.ValueKind == JsonValueKind.String, "false cannot disable the mandatory verified engine preimage backup");
            using var migration = JsonDocument.Parse(File.ReadAllText(report));
            Require(Convert.FromBase64String(migration.RootElement.GetProperty("data").GetProperty("rawInputBase64").GetString()!).SequenceEqual(File.ReadAllBytes(inputFile)), "file import preserves original BOM bytes and does not rewrite the source");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
}
