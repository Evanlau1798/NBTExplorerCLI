using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Verification.CliScanTests;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CliLegacyImportTests
{
    private static string World()
    { var root = ScratchDirectory.CreateChild(); LegacyImportMappingTests.BossWorld(root); return root; }
    private static string Input(string work, byte[] bytes)
    { var file = Path.Combine(work, "legacy-input.json"); File.WriteAllBytes(file, bytes); return file; }
    private static Invocation Import(string work, string world, string profile, string input, params string[] args)
        => Run(work, ["import-legacy", world, "--profile", profile, "--input", input, "--source-language", "en_us", "--target-language", "zh_tw", "--state-root", Path.Combine(work, "state"), .. args]);
    private static Invocation Report(string work, string world, string id, params string[] args)
        => Run(work, ["import-legacy", world, "--import-id", id, "--state-root", Path.Combine(work, "state"), .. args]);
    public static void MappedReportsExportCanonicalBytesAndCreateOneReenterableSession()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            var bytes = LegacyImportMappingTests.Input(); var input = Input(work, bytes); var export = Path.Combine(work, "report.json"); var before = File.ReadAllBytes(Path.Combine(world, "level.dat"));
            using var imported = Success(Import(work, world, "command-session", input, "--acknowledge-world-rebind", "--create-session", "--output", export));
            var data = imported.RootElement.GetProperty("data"); var id = data.GetProperty("report").GetProperty("importId").GetString()!; var session = data.GetProperty("sessionId").GetGuid();
            var report = LegacyImportJson.Read(File.ReadAllText(export));
            Require(report.Complete && report.Items.Single().Decision == LegacyImportDecision.Mapped && Convert.FromBase64String(report.RawInputBase64).SequenceEqual(bytes)
                && data.GetProperty("status").GetProperty("translated").GetInt32() == 1, "mapped input exports the canonical raw report and creates a validated translated session");
            using var replay = Success(Report(work, world, id, "--create-session"));
            Require(replay.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid() == session
                && replay.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64() == 1, "report identity resumes the same session without duplicate operations");
            Require(File.ReadAllBytes(input).SequenceEqual(bytes) && File.ReadAllBytes(Path.Combine(world, "level.dat")).SequenceEqual(before), "import retains input files and never applies world changes");
            using var help = Success(Run(work, "help", "import-legacy")); Require(help.RootElement.GetProperty("data").GetProperty("options").EnumerateArray().Any(option => option.GetString() == "--create-session"), "machine help exposes the public importer");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MissingEvidenceAndConflictsKeepTheirReportsAndNeverCreateSessions()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            var input = Input(work, LegacyImportMappingTests.Input()); var unacknowledged = Import(work, world, "command-session", input, "--create-session"); using var missing = JsonDocument.Parse(unacknowledged.Output);
            Require(unacknowledged.Exit == 4 && missing.RootElement.GetProperty("data").GetProperty("report").GetProperty("items")[0].GetProperty("decision").GetString() == "rescanRequired", "historical WorldPath is never treated as rebind permission");
            input = Input(work, LegacyImportMappingTests.Input(original: "different")); var conflict = Import(work, world, "command-session", input, "--acknowledge-world-rebind", "--create-session"); using var rejected = JsonDocument.Parse(conflict.Output);
            Require(conflict.Exit == 3 && rejected.RootElement.GetProperty("data").GetProperty("report").GetProperty("items")[0].GetProperty("decision").GetString() == "conflict"
                && !Directory.Exists(Path.Combine(work, "state", "sessions")), "changed original text returns a durable conflict report without a writable session");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void UserMetadataAndInvalidUtf8AreRetainedWithoutAutomaticAdoption()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("舊備註🙂\n保留原始資料"); var input = Input(work, bytes); var output = Path.Combine(work, "notes-report.json");
            using var notes = Success(Import(work, world, "notes-text", input, "--output", output));
            var report = LegacyImportJson.Read(File.ReadAllText(output)); Require(report.Items.Single().Decision == LegacyImportDecision.Retained && Convert.FromBase64String(report.RawInputBase64).SequenceEqual(bytes), "notes retain exact UTF-8 bytes and are not silently appended to a session");
            var settings = Input(work, Encoding.UTF8.GetBytes("{\"SourceLanguage\":\"en_us\",\"TargetLanguage\":\"zh_tw\",\"UnknownUserPreference\":true}"));
            using var config = Success(Import(work, world, "agent-config", settings));
            Require(!File.Exists(Path.Combine(work, "state", "agent", "configuration.json")) && !Directory.Exists(Path.Combine(work, "state", "sessions")), "legacy settings are retention data rather than newly confirmed configuration");
            var invalid = Input(work, [0xff, 0x00]); var malformed = Import(work, world, "notes-text", invalid); using var raw = JsonDocument.Parse(malformed.Output);
            var id = raw.RootElement.GetProperty("data").GetProperty("report").GetProperty("importId").GetString()!; var stored = LegacyImportJson.Read(File.ReadAllText(Path.Combine(work, "state", "imports", "import-" + id + ".json")));
            Require(malformed.Exit == 5 && Convert.FromBase64String(stored.RawInputBase64).SequenceEqual(new byte[] { 0xff, 0x00 }), "invalid legacy encoding still has a retained rejection report");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void StaleProjectionAndPublicationFailuresRemainRecoverableByImportId()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            var input = Input(work, LegacyImportMappingTests.Input()); var sessions = Path.Combine(work, "state", "sessions"); Directory.CreateDirectory(sessions); var marker = Path.Combine(sessions, "user.txt"); File.WriteAllText(marker, "keep");
            var blocked = Import(work, world, "command-session", input, "--acknowledge-world-rebind", "--create-session"); using var failure = JsonDocument.Parse(blocked.Output);
            var data = failure.RootElement.GetProperty("data");
            Require(blocked.Exit == 5 && data.ValueKind == JsonValueKind.Object && data.GetProperty("repair").GetString() == "inspect-or-resume-import-session" && File.ReadAllText(marker) == "keep", "a projection failure retains its durable report handle and user-owned contents");
            var id = data.GetProperty("report").GetProperty("importId").GetString()!; File.Delete(marker);
            using var resumed = Success(Report(work, world, id, "--create-session")); var session = resumed.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
            LegacyImportMappingTests.BossWorld(world, "changed");
            var stale = Report(work, world, id, "--create-session"); using var rejected = JsonDocument.Parse(stale.Output);
            Require(stale.Exit == 3 && rejected.RootElement.GetProperty("data").GetProperty("report").GetProperty("importId").GetString() == id, "a stored mapping cannot rebind to a changed snapshot");
            using var retained = Success(Report(work, world, id)); Require(retained.RootElement.GetProperty("data").GetProperty("freshness").GetString() == "not-checked" && session != Guid.Empty, "historical report lookup remains available without claiming current world freshness");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void CandidateFanoutLimitsAreResourcesAndPublishNoPartialReport()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world, componentJson: JsonSerializer.Serialize(Enumerable.Range(0, 65).Select(index => new { text = "Fragment" + index })));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { WorldPath = "old path is metadata", Entries = Enumerable.Range(0, 65).Select(index => new
                { Id = "id" + index, Path = "level.dat#Data/CustomBossEvents/test:event/Name", Original = "Hello", Type = "bossbar", Context = "Boss 血條 (test:event)" }) });
            var result = Import(work, world, "command-session", Input(work, bytes), "--acknowledge-world-rebind");
            Require(result.Exit == 6 && Directory.GetFiles(Path.Combine(work, "state", "imports"), "import-*.json").Length == 0, "aggregate mapping candidates fail as ResourceLimit without a partial report: " + result.Output);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void InvalidModesAndOversizedRawInputsRejectBeforeRepositories()
    {
        var world = World(); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = Input(work, LegacyImportMappingTests.Input());
            Require(Import(work, world, "numeric-unknown", file).Exit == 2 && !Directory.Exists(Path.Combine(work, "state")), "unknown profiles do not create repositories");
            Require(Run(work, "import-legacy", world, "--input", file, "--profile", "command-session", "--import-id", new string('a', 64), "--state-root", Path.Combine(work, "state")).Exit == 2, "input and report modes are exclusive");
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write)) stream.SetLength(LegacyInputReader.MaximumInputBytes + 1L);
            Require(Import(work, world, "notes-text", file).Exit == 6 && !Directory.Exists(Path.Combine(work, "state")), "legacy byte cap is applied before reading/decoding or repository creation");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
