using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class LegacyImportMappingTests
{
    private sealed class InterleavedStore(ITranslationSessionStore inner, Action<Guid> change) : ITranslationSessionStore
    {
        private int reads;
        public byte[]? Read(Guid id) { if (Interlocked.Increment(ref reads) == 3) change(id); return inner.Read(id); }
        public void Write(Guid id, ReadOnlyMemory<byte> bytes, Hash256? expected) => inner.Write(id, bytes, expected);
    }
    public static void ProjectionCannotRebaseOverUserOperation()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild(); var stateRoot = ScratchDirectory.CreateChild();
        try
        {
            BossWorld(root); var snapshot = Query(root, cache); var importer = new LegacyImportService(new FileSystemLegacyImportStore(imports));
            var report = importer.Import(LegacyInputProfile.CommandSession, Input(), snapshot, "en_us", "zh_tw", true);
            var store = new FileSystemTranslationSessionStore(stateRoot); var normal = new TranslationSessionService(store); Guid target = default;
            var interleaved = new TranslationSessionService(new InterleavedStore(store, id =>
            {
                target = id; var state = normal.Read(id); normal.Execute(new(Guid.NewGuid(), id, state.Revision, SessionCommandKind.Note, Text: "user operation", Origin: "human"));
            }));
            Reject<SessionReplyRejectedException>(() => importer.CreateSession(report, snapshot, interleaved));
            Require(normal.Status(target).Pending == 1 && normal.Read(target).Workspace!.Notes.Single().Text == "user operation", "user operation after projection read makes original reply stale, without silently rebasing or overwriting it");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); Directory.Delete(stateRoot, true); }
    }
    internal static void BossWorld(string root, string text = "Hello", string? componentJson = null)
    {
        var level = WorldMetadataTests.Level(3953, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)level.Root).Entries.Single().Value;
        var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString(componentJson ?? JsonSerializer.Serialize(new { text })))]))]);
        Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
    }
    internal static byte[] Input(string? translated = "你好", string original = "Hello", string? hash = null, string? context = "Boss 血條 (test:event)", int lines = 1, string path = "level.dat#Data/CustomBossEvents/test:event/Name")
        => JsonSerializer.SerializeToUtf8Bytes(new { WorldPath = "historical root must not be opened", Entries = new[] { new { Id = "boss", Path = path, Original = original, Translated = translated, Type = "bossbar", Context = context, LineCount = lines, SourceHash = hash, SourceHashKind = hash is null ? null : "raw-artifact-sha256" } }, Notes = "keep", Unknown = new { user = true } });
    internal static WorldQuerySnapshot Query(string root, string cache) => new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)).QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
    public static void CandidateFanoutHasAnAggregateBudget()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild();
        try
        {
            BossWorld(root, componentJson: JsonSerializer.Serialize(Enumerable.Range(0, 65).Select(index => new { text = "Fragment" + index })));
            var snapshot = Query(root, cache); var importer = new LegacyImportService(new FileSystemLegacyImportStore(imports));
            var input = JsonSerializer.SerializeToUtf8Bytes(new { WorldPath = "old", Entries = Enumerable.Range(0, 65).Select(index => new { Id = "id" + index, Path = "level.dat#Data/CustomBossEvents/test:event/Name", Original = "Hello", Type = "bossbar", Context = "Boss 血條 (test:event)" }) });
            Reject<IOException>(() => importer.Import(LegacyInputProfile.CommandSession, input, snapshot, "en_us", "zh_tw", true));
            Require(Directory.GetFiles(imports, "import-*.json").Length == 0, "aggregate ambiguous candidate expansion fails explicitly before publishing a partial report");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); }
    }
    public static void UniqueMappingUsesNormalValidationAndNewSnapshot()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild(); var sessionsRoot = ScratchDirectory.CreateChild();
        try
        {
            BossWorld(root); var snapshot = Query(root, cache); var bytes = Input(); var importer = new LegacyImportService(new FileSystemLegacyImportStore(imports));
            var report = importer.Import(LegacyInputProfile.CommandSession, bytes, snapshot, "en_us", "zh_tw", true); var item = report.Items.Single();
            Require(report.Complete && item.Decision == LegacyImportDecision.Mapped && item.Candidates.Single().NodePath.ToJsonPointer() == "/Data/CustomBossEvents/test:event/Name"
                && item.LegacySourceHash is null && item.CurrentSourceHash == Hash256.Compute(File.ReadAllBytes(Path.Combine(root, "level.dat"))), "unique legacy type/context/path/text maps to exact scan address with separate missing historical and verified current hash");
            var sessions = new TranslationSessionService(new FileSystemTranslationSessionStore(sessionsRoot)); var id = importer.CreateSession(report, snapshot, sessions);
            Require(sessions.Status(id).Translated == 1 && sessions.ValidatedPage(id, 0).Valid && sessions.ValidatedPage(id, 0).Value!.Translations.Single().Reply.Text == "你好", "mapped decoded text enters the ordinary durable session and shared validator");
            var revision = sessions.Read(id).Revision; Require(importer.CreateSession(report, snapshot, sessions) == id && sessions.Read(id).Revision == revision, "projection retry does not duplicate session operations");
            var reset = sessions.PreviewReset(id, [item.SlotId!]); sessions.Execute(new(Guid.NewGuid(), id, reset.Revision, SessionCommandKind.Reset, Confirmed: true, ResetSlots: reset.Slots));
            Reject<IOException>(() => importer.CreateSession(report, snapshot, sessions));
            Require(sessions.Status(id).Pending == 1, "re-import refuses to overwrite user changes made after the projected session");
            Reject<IOException>(() => importer.CreateSession(report with { Items = [item with { Translation = "forged" }] }, snapshot, sessions));
            var before = File.ReadAllBytes(Path.Combine(root, "level.dat")); var bound = Input(hash: Hash256.Compute(before).Hex); var old = importer.Import(LegacyInputProfile.CommandSession, bound, snapshot, "en_us", "zh_tw", true);
            BossWorld(root, "Changed"); var changed = Query(root, cache); var stale = importer.Import(LegacyInputProfile.CommandSession, bound, changed, "en_us", "zh_tw", true);
            Require(stale.ImportId != old.ImportId && stale.Items.Single().Decision == LegacyImportDecision.Conflict && new FileSystemLegacyImportStore(imports).Read(old.ImportId) is not null, "changed target snapshot keeps old provenance and never reuses a stale writable mapping");
            Reject<IOException>(() => importer.CreateSession(report, changed, sessions));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); Directory.Delete(sessionsRoot, true); }
    }
    public static void MissingEvidenceAndMalformedInputNeverGuess()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild();
        try
        {
            BossWorld(root); var snapshot = Query(root, cache); var importer = new LegacyImportService(new FileSystemLegacyImportStore(imports));
            LegacyImportBundle Import(byte[] bytes, bool acknowledged = true) => importer.Import(LegacyInputProfile.CommandSession, bytes, snapshot, "en_us", "zh_tw", acknowledged);
            foreach (var input in new[] { Input(context: null), Input(lines: 2), Input(path: "r.0.0.mca#Name"), Input(path: "pack.zip#data/test/text"), Input(path: "level.dat#Data/../Name") })
                Require(Import(input).Items.All(item => item.Decision != LegacyImportDecision.Mapped), "missing context/dimension/entry/shape and ambiguous legacy path never become writable mappings");
            Require(Import(Input(), false).Items.Single().Decision == LegacyImportDecision.RescanRequired, "historical WorldPath cannot authorize target world rebind");
            Require(Import(Input(original: "Other")).Items.Single().Decision == LegacyImportDecision.Conflict, "changed original text at exact location is a conflict");
            var hash = Hash256.Compute(File.ReadAllBytes(Path.Combine(root, "level.dat"))).Hex;
            var untypedHash = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Input(hash: hash)).Replace("\"SourceHashKind\":\"raw-artifact-sha256\"", "\"SourceHashKind\":null", StringComparison.Ordinal));
            Require(Import(untypedHash).Items.Single().Decision == LegacyImportDecision.Unsupported, "a hash without identified historical meaning cannot be reinterpreted as artifact preimage");
            var duplicates = JsonSerializer.SerializeToUtf8Bytes(new { WorldPath = "old", Entries = new[] {
                new { Id = "one", Path = "level.dat#Data/CustomBossEvents/test:event/Name", Original = "Hello", Translated = "First", Type = "bossbar", Context = "Boss 血條 (test:event)" },
                new { Id = "two", Path = "level.dat#Data/CustomBossEvents/test:event/Name", Original = "Hello", Translated = "Second", Type = "bossbar", Context = "Boss 血條 (test:event)" } } });
            Require(Import(duplicates).Items.All(item => item.Decision == LegacyImportDecision.Conflict), "different old IDs cannot assign competing translations to the same new slot");
            Put(root, "region/r.0.0.mca", []); var incomplete = Query(root, cache);
            Require(!incomplete.Manifest.CoverageComplete && importer.Import(LegacyInputProfile.CommandSession, Input(), incomplete, "en_us", "zh_tw", true).Items.Single().Decision == LegacyImportDecision.RescanRequired,
                "failed sources cannot prove global uniqueness by omission");
            foreach (var bytes in new[] { Encoding.UTF8.GetBytes("{\"WorldPath\":\"a\",\"WorldPath\":\"b\",\"Entries\":[]}"), Encoding.UTF8.GetBytes("{\"WorldPath\":\"a\",\"Entries\":[]}"), Encoding.UTF8.GetBytes("{\"WorldPath\":\"a\",\"Entries\":[{\"Id\":\"x\",\"Original\":\"x\",\"LineCount\":\"bad\"}]}"), new byte[] { 0xff } })
            {
                var report = Import(bytes); Require(!report.Complete && report.Items.Any() && report.Items.All(item => item.Decision is LegacyImportDecision.Rejected or LegacyImportDecision.Unsupported) && Convert.FromBase64String(report.RawInputBase64).SequenceEqual(bytes), "malformed/duplicate/empty input is reported and retained instead of empty-success migration");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); }
    }
}
