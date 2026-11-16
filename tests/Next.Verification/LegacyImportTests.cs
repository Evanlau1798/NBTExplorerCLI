using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldQueryTests;

namespace Next.Verification;

internal static class LegacyImportTests
{
    private sealed class FailedFlush : ArtifactFileIo
    {
        public override void Flush(FileStream output) => throw new IOException("Injected legacy bundle flush failure");
    }
    private sealed class LostReceiptStore(ILegacyImportStore inner) : ILegacyImportStore
    {
        public byte[]? Read(Hash256 id) => inner.Read(id);
        public void Save(Hash256 id, ReadOnlyMemory<byte> bytes) { inner.Save(id, bytes); throw new IOException("Lost import receipt after durable save"); }
    }
    public static void FailedPersistenceRetainsInputAndRetries()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, cache); var bytes = Encoding.UTF8.GetBytes("user note {{T0}}");
            _ = new FileSystemLegacyImportStore(imports);
            var failed = new LegacyImportService(new FileSystemLegacyImportStore(imports, new FailedFlush()));
            CandidateIoTests.Reject<IOException>(() => failed.Import(LegacyInputProfile.NotesText, bytes, snapshot, "en_us", "zh_tw", true));
            Require(Directory.GetFiles(imports, "import-*.json").Length == 0 && Directory.GetFiles(imports, "*.candidate").Length == 0, "failed flush never exposes a half bundle or discards the source input");
            var store = new FileSystemLegacyImportStore(imports); var service = new LegacyImportService(store);
            var bundle = service.Import(LegacyInputProfile.NotesText, bytes, snapshot, "en_us", "zh_tw", true);
            Require(Convert.FromBase64String(bundle.RawInputBase64).SequenceEqual(bytes) && service.Import(LegacyInputProfile.NotesText, bytes, snapshot, "en_us", "zh_tw", true).ImportId == bundle.ImportId, "retry retains identical raw data and publishes one durable report");
            var changed = LegacyImportJson.Write(bundle with { Items = [bundle.Items[0] with { Reason = "tampered" }] });
            CandidateIoTests.Reject<IOException>(() => store.Save(bundle.ImportId, Encoding.UTF8.GetBytes(changed)));
            var other = Encoding.UTF8.GetBytes("different retained input");
            CandidateIoTests.Reject<IOException>(() => new LegacyImportService(new LostReceiptStore(store)).Import(LegacyInputProfile.NotesText, other, snapshot, "en_us", "zh_tw", true));
            var recovered = service.Import(LegacyInputProfile.NotesText, other, snapshot, "en_us", "zh_tw", true);
            Require(Convert.FromBase64String(recovered.RawInputBase64).SequenceEqual(other) && Directory.GetFiles(imports, "import-*.json").Length == 2, "lost receipt retry reads one whole saved bundle without overwriting previous import");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); }
    }
    public static void FixedLegacyInputsAreRetained(string fixtureRoot) => Fixed(fixtureRoot).GetAwaiter().GetResult();
    public static void ReportCodecEnforcesAggregateCandidates()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, cache); var service = new LegacyImportService(new FileSystemLegacyImportStore(imports));
            var report = service.Import(LegacyInputProfile.NotesText, Encoding.UTF8.GetBytes("retained"), snapshot, "en_us", "zh_tw", true);
            var address = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt), TypedPath.Root, "candidate");
            var excessive = report with { Items = [report.Items[0] with { Candidates = System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(address, 4097)) }] };
            CandidateIoTests.Reject<System.Text.Json.JsonException>(() => LegacyImportJson.Write(excessive));
            var valid = report with { Items = [report.Items[0] with { Candidates = System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(address, 4096)) }] };
            var json = LegacyImportJson.Write(valid);
            CandidateIoTests.Reject<System.Text.Json.JsonException>(() => LegacyImportJson.Read(json.Replace("\"candidates\":[", "\"candidates\":[" + SourceAddressJson.Write(address) + ",", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); }
    }
    private static async Task Fixed(string fixtureRoot)
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild(); var imports = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = (await new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)).QueryAsync(new(new(SnapshotScope.All)))).Snapshot!;
            var store = new FileSystemLegacyImportStore(imports); var service = new LegacyImportService(store);
            var path = Path.Combine(fixtureRoot, "legacy-user-data", ".nbtcli_translate", "session.json"); var bytes = File.ReadAllBytes(path); var hash = Hash256.Compute(bytes);
            var bundle = service.Import(LegacyInputProfile.CommandSession, bytes, snapshot, "en_us", "zh_tw", true);
            Require(bundle.Items.Length == 2 && bundle.Items.All(item => item.Decision == LegacyImportDecision.Conflict && item.Reason == "DuplicateLegacyId")
                && bundle.Items.Select(item => item.Translation).SequenceEqual(new[] { "你好", "您好" }) && !bundle.Complete
                && Convert.FromBase64String(bundle.RawInputBase64).SequenceEqual(bytes), "duplicate legacy IDs never pair by order or discard either translation, and raw provenance remains exact");
            Require(service.Import(LegacyInputProfile.CommandSession, bytes, snapshot, "en_us", "zh_tw", true).ImportId == bundle.ImportId && Hash256.Compute(File.ReadAllBytes(path)) == hash,
                "same identified input and target snapshot reuse one durable report without changing the old original");
            var samples = new[] { (LegacyInputProfile.ReplyArray, ".nbtcli_translate/translated.json", LegacyImportDecision.Unsupported), (LegacyInputProfile.Glossary, "glossary.json", LegacyImportDecision.Retained),
                (LegacyInputProfile.KeyMapping, "key-mapping.json", LegacyImportDecision.Retained), (LegacyInputProfile.AgentConfig, ".nbtcli_cache/agent_config.json", LegacyImportDecision.Retained),
                (LegacyInputProfile.Questionnaire, ".nbtcli_cache/agent_questionnaire.json", LegacyImportDecision.Retained), (LegacyInputProfile.NotesText, ".nbtcli_cache/notes.txt", LegacyImportDecision.Retained), (LegacyInputProfile.ScanCache, ".nbtcli_cache/scan-index.bin", LegacyImportDecision.RescanRequired) };
            foreach (var (profile, relative, expected) in samples)
            {
                var original = File.ReadAllBytes(Path.Combine(fixtureRoot, "legacy-user-data", relative)); var report = service.Import(profile, original, snapshot, "en_us", "zh_tw", true);
                Require(report.Items.All(item => item.Decision == expected) && Convert.FromBase64String(report.RawInputBase64).SequenceEqual(original) && store.Read(report.ImportId) is not null,
                    "explicit input profile retains original settings/notes/glossary/unrecognized reply/cache data without schema guessing or destructive migration");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(imports, true); }
    }
}
