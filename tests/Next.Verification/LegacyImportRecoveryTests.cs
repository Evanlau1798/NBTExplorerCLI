using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CliScanTests;

namespace Next.Verification;

internal static class LegacyImportRecoveryTests
{
    private sealed class StopReplyStore(ITranslationSessionStore inner, int stopWrite) : ITranslationSessionStore
    {
        private int writes;
        public byte[]? Read(Guid id) => inner.Read(id);
        public void Write(Guid id, ReadOnlyMemory<byte> bytes, Hash256? expected)
        {
            if (++writes == stopWrite) throw new IOException("Stop projection before the selected reply write");
            inner.Write(id, bytes, expected);
        }
    }
    public static void ProjectionRejectsCollidingOperationIdsWithDifferentContent()
    {
        var refusals = new List<bool>();
        foreach (var note in new[] { true, false })
        {
            var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
            try
            {
                LegacyImportMappingTests.BossWorld(world); var snapshot = LegacyImportMappingTests.Query(world, Path.Combine(work, "cache"));
                var importer = new LegacyImportService(new FileSystemLegacyImportStore(Path.Combine(work, "imports")));
                var report = importer.Import(LegacyInputProfile.CommandSession, LegacyImportMappingTests.Input(), snapshot, "en_us", "zh_tw", true);
                var store = new FileSystemTranslationSessionStore(Path.Combine(work, "sessions"));
                CandidateIoTests.Reject<IOException>(() => importer.CreateSession(report, snapshot, new(new StopReplyStore(store, 2))));
                var sessions = new TranslationSessionService(store); var id = LegacyImportService.SessionId(report.ImportId);
                var operation = new Guid(Convert.FromHexString(TextRevision.Compute(report.ImportId.Hex + ":page:0").Hex).AsSpan(0, 16));
                if (note) sessions.Execute(new(operation, id, 0, SessionCommandKind.Note, Text: "keep user work", Origin: "human"));
                else
                {
                    var page = sessions.Export(id, 0);
                    var reply = new SessionReply(operation, id, page.Revision, page.SnapshotId, page.Page, page.PageId, page.TargetLocale,
                        [new(page.Slots[0].SlotId, page.Slots[0].SourceTextHash, SessionReplyState.Translated, "user translation")]);
                    sessions.Submit(id, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
                }
                var before = store.Read(id)!;
                var refused = false;
                try { importer.CreateSession(report, snapshot, sessions); }
                catch (ArtifactConflictException) { refused = true; }
                refusals.Add(refused);
                Require(store.Read(id)!.SequenceEqual(before), "same-ID different-kind or different-reply replay refuses without rewriting accepted user work");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
        Require(refusals.All(value => value), "same-ID wrong-kind and wrong-reply must both conflict: " + string.Join(",", refusals));
    }
    public static void MultiPageProjectionResumesExactRepliesAcrossAnUntranslatedPage()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            var level = WorldMetadataTests.Level(3953, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)level.Root).Entries.Single().Value;
            var bosses = new NbtCompound(Enumerable.Range(0, 3).Select(index => new NbtEntry("test:event" + index,
                new NbtCompound([new("Name", new NbtString("{\"text\":\"Hello\"}"))]))));
            WorldDiscoveryTests.Put(world, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { WorldPath = "historical metadata", Entries = Enumerable.Range(0, 3).Select(index => new { Id = "item" + index, Original = "Hello", Translated = index == 1 ? null : "譯文" + index,
                Path = "level.dat#Data/CustomBossEvents/test:event" + index + "/Name", Type = "bossbar", Context = "Boss 血條 (test:event" + index + ")" }) });
            var snapshot = LegacyImportMappingTests.Query(world, Path.Combine(work, "cache")); var importer = new LegacyImportService(new FileSystemLegacyImportStore(Path.Combine(work, "imports")));
            var report = importer.Import(LegacyInputProfile.CommandSession, bytes, snapshot, "en_us", "zh_tw", true); var store = new FileSystemTranslationSessionStore(Path.Combine(work, "sessions"));
            Require(report.Items.Length == 3 && report.Items.All(item => item.Decision == LegacyImportDecision.Mapped), "control input maps all three exact bossbar targets before fault injection");
            CandidateIoTests.Reject<IOException>(() => importer.CreateSession(report, snapshot, new(new StopReplyStore(store, 3))));
            var service = new TranslationSessionService(store); var id = LegacyImportService.SessionId(report.ImportId);
            Require(service.Status(id).Revision == 1 && service.Status(id).Translated == 1, "the first reply persists before the selected later write fails");
            Require(importer.CreateSession(report, snapshot, service) == id && service.Status(id).Revision == 2
                && service.Status(id).Translated == 2 && service.Status(id).Pending == 1, "reentry validates earlier bytes and resumes only the missing reply across a pending page");
            var before = store.Read(id)!; _ = importer.CreateSession(report, snapshot, service);
            Require(store.Read(id)!.SequenceEqual(before), "complete multipage replay neither rewrites nor counts again");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private static Invocation Import(string world, string work, string file, params string[] args) => Run(work,
        ["import-legacy", world, "--input", file, "--profile", "notes-text", "--source-language", "en_us", "--target-language", "zh_tw", "--state-root", Path.Combine(work, "state"), .. args]);
    public static void PersistenceFailuresRetainAnImportIdentityAndExactRetry()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world); var file = Path.Combine(work, "notes.txt"); File.WriteAllText(file, "keep original notes");
            var root = Path.Combine(work, "state", "imports"); _ = new FileSystemLegacyImportStore(root); Invocation blocked;
            using (var locked = File.Open(Path.Combine(root, "owner.json"), FileMode.Open, FileAccess.Read, FileShare.None)) blocked = Import(world, work, file);
            using var failure = JsonDocument.Parse(blocked.Output); var data = failure.RootElement.GetProperty("data");
            Require(blocked.Exit == 5 && data.ValueKind == JsonValueKind.Object && data.GetProperty("repair").GetString() == "retry-same-import-input"
                && data.GetProperty("importId").GetString() is { Length: 64 }, "native pre-save I/O failure returns a deterministic recovery identity and exact retry: " + blocked.Output);
            var id = data.GetProperty("importId").GetString()!;
            Require(Directory.GetFiles(root, "import-*.json").Length == 0, "blocked ownership read publishes no report");
            using var saved = Success(Import(world, work, file));
            Require(saved.RootElement.GetProperty("data").GetProperty("report").GetProperty("importId").GetString() == id, "same input and snapshot retry retains the same identity");
            using (var locked = File.Open(Path.Combine(root, "import-" + id + ".json"), FileMode.Open, FileAccess.Read, FileShare.None)) blocked = Import(world, work, file);
            using var afterSave = JsonDocument.Parse(blocked.Output);
            Require(blocked.Exit == 5 && afterSave.RootElement.GetProperty("data").GetProperty("importId").GetString() == id, "I/O failure when a report already exists preserves its lookup identity");
            using var lookup = Success(Run(work, "import-legacy", world, "--import-id", id, "--state-root", Path.Combine(work, "state")));
            Require(File.ReadAllText(file) == "keep original notes" && Directory.GetFiles(root, "import-*.json").Length == 1, "unlocked retry retrieves one complete report and retains caller input");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void LostReportHandlesRemainDiscoverableAfterWorldChanges()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world); var file = Path.Combine(work, "notes.txt"); File.WriteAllText(file, "retained raw notes");
            Require(Import(world, work, file).Exit == 0, "publish a report, deliberately discarding its output handle");
            LegacyImportMappingTests.BossWorld(world, "changed");
            using var listed = Success(Run(work, "import-legacy", world, "--list", "--limit", "1", "--state-root", Path.Combine(work, "state")));
            var reports = listed.RootElement.GetProperty("data").GetProperty("reports");
            Require(reports.GetArrayLength() == 1 && !reports[0].TryGetProperty("rawInputBase64", out _), "recovery listing exposes opaque report metadata without raw content");
            var id = reports[0].GetProperty("importId").GetString()!;
            using var recovered = Success(Run(work, "import-legacy", world, "--import-id", id, "--state-root", Path.Combine(work, "state")));
            Require(recovered.RootElement.GetProperty("data").GetProperty("freshness").GetString() == "not-checked", "old reports remain recoverable without their original snapshot or output");
            Require(Import(world, work, file, "--list").Exit == 2, "listing and new import mode are mutually exclusive");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void AnUntranslatedPageCannotBorrowAnImportOperationIdentity()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world); var snapshot = LegacyImportMappingTests.Query(world, Path.Combine(work, "cache"));
            var importer = new LegacyImportService(new FileSystemLegacyImportStore(Path.Combine(work, "imports")));
            var report = importer.Import(LegacyInputProfile.CommandSession, LegacyImportMappingTests.Input(translated: null), snapshot, "en_us", "zh_tw", true);
            var store = new FileSystemTranslationSessionStore(Path.Combine(work, "sessions")); var sessions = new TranslationSessionService(store);
            var id = importer.CreateSession(report, snapshot, sessions);
            Require(sessions.Status(id).Pending == 1 && sessions.Status(id).Revision == 0, "the untranslated page initially has no reply operation");
            var borrowed = new Guid(Convert.FromHexString(TextRevision.Compute(report.ImportId.Hex + ":page:0").Hex).AsSpan(0, 16));
            sessions.Execute(new(borrowed, id, 0, SessionCommandKind.Note, Text: "keep this note", Origin: "human"));
            var before = store.Read(id)!;
            CandidateIoTests.Reject<ArtifactConflictException>(() => importer.CreateSession(report, snapshot, sessions));
            Require(store.Read(id)!.SequenceEqual(before) && sessions.Status(id).Pending == 1
                && sessions.Read(id).Workspace!.Notes.Single().Text == "keep this note", "undeclared reply identity conflicts without changing pending work or the accepted note");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void RecoveryListingNeverReadsReportBodiesBeforeOrAfterPagination()
    {
        var world = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        try
        {
            LegacyImportMappingTests.BossWorld(world); var file = Path.Combine(work, "notes.txt");
            foreach (var text in new[] { "first raw report", "second raw report" })
            { File.WriteAllText(file, text); using var imported = Success(Import(world, work, file)); }
            var root = Path.Combine(work, "state", "imports"); var ids = new FileSystemLegacyImportStore(root).ListIds();
            using (var first = File.Open(Path.Combine(root, "import-" + ids[0].Hex + ".json"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (var second = File.Open(Path.Combine(root, "import-" + ids[1].Hex + ".json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                using var listed = Success(Run(work, "import-legacy", world, "--list", "--offset", "1", "--limit", "1", "--state-root", Path.Combine(work, "state")));
                var data = listed.RootElement.GetProperty("data"); var reports = data.GetProperty("reports");
                Require(reports.GetArrayLength() == 1 && reports[0].GetProperty("importId").GetString() == ids[1].Hex
                    && data.GetProperty("scope").GetString() == "import-repository" && !data.GetProperty("contentsValidated").GetBoolean(), "paged handles require zero report-body reads and do not claim world/content validation");
                Require(Run(work, "import-legacy", world, "--import-id", ids[1].Hex, "--state-root", Path.Combine(work, "state")).Exit == 5, "control: full report lookup really is blocked by the held native lock");
            }
            using var retrieved = Success(Run(work, "import-legacy", world, "--import-id", ids[1].Hex, "--state-root", Path.Combine(work, "state")));
            Require(retrieved.RootElement.GetProperty("data").GetProperty("report").GetProperty("importId").GetString() == ids[1].Hex, "unlocked explicit lookup validates the selected full report");
            var otherWorld = Path.Combine(work, "another-world"); Directory.CreateDirectory(otherWorld); LegacyImportMappingTests.BossWorld(otherWorld);
            using var repositoryIndex = Success(Run(work, "import-legacy", otherWorld, "--list", "--state-root", Path.Combine(work, "state")));
            Require(repositoryIndex.RootElement.GetProperty("data").GetProperty("reports").GetArrayLength() == 2
                && Run(work, "import-legacy", otherWorld, "--import-id", ids[1].Hex, "--state-root", Path.Combine(work, "state")).Exit == 3,
                "repository handles grant no authority: foreign-world full report lookup still conflicts");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}
