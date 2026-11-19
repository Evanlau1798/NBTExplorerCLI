using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.WorldQueryTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldQueryParityTests
{
    private static byte[] SignRegion()
    {
        var sign = C(E("id", S("minecraft:sign")), E("front_text", C(E("messages", L(Text("Same", false), Text("", false))))));
        var payload = CompressedNbt.WriteDocument(new("", C(E("DataVersion", new NbtInt(3953)), E("block_entities", L(sign)))), NbtCompression.Zlib);
        var bytes = new byte[12288]; BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x201);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8192), (uint)payload.Length + 1); bytes[8196] = 2; payload.CopyTo(bytes, 8197); return bytes;
    }
    public static void MixedCodecsWorkersAndCacheModes() => Mixed().GetAwaiter().GetResult();
    private static async Task Mixed()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "region/r.-1.0.mca", SignRegion()); Put(root, "DIM-1/region/r.-1.0.mca", SignRegion()); Put(root, "data/broken.dat", [1, 2, 3]);
            Put(root, "datapacks/literal.zip", Zip(("pack.mcmeta", Format48Pack), ("data/models/function/run.mcfunction", "tellraw @s \"Same\"")));
            Put(root, "arbitrary.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/models/lang/en_us.json", "{\"item.custom\":\"Same\"}")));
            var inventory = new WorldInventory(root, World); var hashes = Hashes(inventory);
            var service = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var request = new WorldQueryRequest(new(SnapshotScope.All, new(MaxDecodedBytes: 1_048_576)), Execution: new(1, 2, 4_194_304));
            string[]? expected = null;
            foreach (var workers in new[] { 1, 2, 8 })
            {
                var full = await service.QueryAsync(request with { ForceFull = true, Execution = request.Execution! with { Workers = workers } });
                var rows = full.Snapshot!.ReadSources().ToArray(); var semantic = Rows(full.Snapshot);
                Require(!full.Snapshot.Manifest.CoverageComplete && rows.Count(row => row.Source.Location.Artifact.RelativePath == "data/broken.dat" && row.Outcome == SourceOutcome.Failed) == 1,
                    "one real malformed DAT remains a source failure without hiding successful codec results");
                var signs = rows.SelectMany(row => row.Text?.Occurrences ?? []).Where(text => text.Address.SemanticRole == "sign.front.line").ToArray();
                Require(signs.Length == 4 && signs.Select(text => text.Address.Dimension).Distinct().Count() == 2
                    && signs.All(text => text.Address.Chunk == new ChunkPosition(-32, 0))
                    && rows.Count(row => row.Source.Location.Artifact.ContainerEntry is not null) == 4, "two dimensions at the same negative chunk and two literal ZIP identities remain separate through cached codec results");
                Require(expected is null || semantic.SequenceEqual(expected), "full worker1/default/max semantic rows, stable IDs and failure diagnostics are identical"); expected = semantic;
                Require(full.Telemetry.ScannedArtifacts + full.Telemetry.ReusedArtifacts == full.Snapshot.Manifest.Artifacts.Length
                    && full.Telemetry.ScannedSources + full.Telemetry.ReusedSources == rows.Length
                    && full.Scan!.Telemetry.StoredSources == rows.Length, "artifact/source work and semantic result counts have separate conserved telemetry");
                var warm = await service.QueryAsync(request);
                Require(warm.Before.State == CacheState.Fresh && !warm.Snapshot!.Manifest.CoverageComplete && Rows(warm.Snapshot).SequenceEqual(semantic)
                    && warm.Telemetry.ScannedArtifacts == 0, "warm Fresh means current input identity, not complete semantic coverage");
            }
            Put(root, Function(0), Encoding.UTF8.GetBytes("tellraw @s \"Changed\""));
            var incremental = await service.QueryAsync(request); var clean = await service.QueryAsync(request with { ForceFull = true });
            Require(incremental.Telemetry.ScannedArtifacts == 1 && incremental.Telemetry.ReusedArtifacts > 0 && Rows(incremental.Snapshot!).SequenceEqual(Rows(clean.Snapshot!))
                && incremental.Snapshot!.Manifest.SnapshotId == clean.Snapshot!.Manifest.SnapshotId, "real mixed codec incremental and clean full scans agree including unchanged failures");
            var current = Hashes(inventory); hashes[Function(0)] = current[Function(0)];
            Require(hashes.Count == current.Count && hashes.All(pair => current[pair.Key] == pair.Value), "query modes and cache publication never mutate the input file set or bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static Dictionary<string, Hash256> Hashes(IWorldInventory inventory) => inventory.Enumerate().Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))));
    public static void CacheSerializationLimitIsSourceFailure()
    {
        SerializationLimit().GetAwaiter().GetResult();
        AggregateRegionCacheKeepsText().GetAwaiter().GetResult();
        AggregateEncodedCacheBudget();
        CacheRetentionCountsRecordStrings();
    }
    private static void CacheRetentionCountsRecordStrings()
    {
        var artifact = new ArtifactAddress(World, Function(0), ArtifactKind.McFunction);
        var hash = Hash256.Compute("text"u8);
        long Measure(string suffix)
        {
            TypedPath Path(string key) => new([PathSegment.ForKey(key + suffix)]);
            var syntax = new SyntaxLocator("grammar" + suffix, 1, 0, 4, hash.Hex, "encoding" + suffix, "none", Path("syntax"));
            var address = new SourceAddress(artifact, Path("node"), "role" + suffix, "dimension" + suffix, componentPath: Path("component"), syntax: syntax);
            var text = new TextOccurrence(address, "Text", "context" + suffix, true);
            var row = new CachedSource(artifact, WorldSourceRole.Function, null, SourceOutcome.Success, null, new([text], [], [], [], []), null);
            Require(ScanCacheJson.EncodeShardWithinBudget(new(hash, [row]), out var retained) is not null, "small cache samples fit their budgets");
            return retained;
        }
        var extra = new string('x', 512);
        Require(Measure(extra) - Measure("") == 2L * extra.Length * 8, "retained cache bytes count record strings and every path key");
    }
    private static void AggregateEncodedCacheBudget()
    {
        var address = new ArtifactAddress(World, "region/r.0.0.mca", ArtifactKind.Region);
        var path = new TypedPath([PathSegment.ForKey(new string('雪', 5_600_000))]);
        var parseKey = Hash256.Compute("parts"u8);
        string Part(int x)
        {
            var chunk = new ChunkPosition(x, 0);
            var text = new TextOccurrence(new(address, path, "sign.front.line", "minecraft:overworld", chunk), "Text", "sign.front.line", true);
            var row = new CachedSource(address, WorldSourceRole.Terrain, chunk, SourceOutcome.Success, null, new([text], [], [], [], []), null);
            return ScanCacheJson.WriteShard(new(parseKey, [row]));
        }
        var first = Encoding.UTF8.GetBytes(Part(0)); var second = Encoding.UTF8.GetBytes(Part(1));
        var left = Hash256.Compute(first); var right = Hash256.Compute(second);
        Require(first.Length < ScanCacheJson.MaximumStoredShardBytes && first.Length + second.Length > ScanCacheJson.MaximumStoredShardBytes,
            "the aggregate byte bound is reached while each part fits");
        var index = ScanCacheJson.WriteShardIndex(parseKey, [left, right]);
        CandidateIoTests.Reject<System.Text.Json.JsonException>(() => ScanCacheJson.ReadStoredShard(index, 2, hash => hash == left ? first : second));
    }
    private static async Task SerializationLimit()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, Function(0), Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("tellraw @s \"Text\"\n", 12000))));
            var uncached = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [Function(0)]))).ReadSources().Single();
            Require(uncached.Outcome == SourceOutcome.Success && uncached.Text!.Occurrences.Length == 12000, "large input fits extraction before cache serialization: " + uncached.Outcome + "/" + uncached.DiagnosticCode);
            var service = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache));
            var result = await service.QueryAsync(new(new(new([], [], [], [Function(0)]))));
            Require(result.Telemetry.Published && !result.Snapshot!.Manifest.CoverageComplete && result.Snapshot.ReadSources().Single() is { Outcome: SourceOutcome.Failed, DiagnosticCode: "CacheSerializationLimit", Text: null },
                "cache contract byte/token limits are explicit bounded source failures rather than an unhandled exception or partial successful cache: " + result.Snapshot?.ReadSources().Single().DiagnosticCode);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static async Task AggregateRegionCacheKeepsText()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string path = "region/r.0.0.mca";
            const int rowsPerChunk = 9_000;
            var sign = C(E("id", S("minecraft:sign")), E("front_text", C(E("messages", L(Text("Example", false))))));
            var payload = CompressedNbt.WriteDocument(new("", C(E("DataVersion", new NbtInt(3953)),
                E("block_entities", L(Enumerable.Repeat<NbtValue>(sign, rowsPerChunk).ToArray())))), NbtCompression.Zlib);
            var sectors = (payload.Length + 5 + 4095) / 4096;
            var region = new byte[(2 + 2 * sectors) * 4096];
            for (var chunk = 0; chunk < 2; chunk++)
            {
                var offset = 2 + chunk * sectors;
                BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(chunk * 4), (uint)(offset << 8 | sectors));
                BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(offset * 4096), (uint)payload.Length + 1);
                region[offset * 4096 + 4] = 2;
                payload.CopyTo(region, offset * 4096 + 5);
            }
            Put(root, path, region);
            var inventory = new WorldInventory(root, World);
            var scan = new ScanRequest(new([], [], [], [path]));
            var expected = new WorldTextExtraction(inventory, scan).ReadSources().ToArray();
            Require(expected.Count(row => row.Outcome == SourceOutcome.Success) == 2
                && expected.Sum(row => row.Text?.Occurrences.Length ?? 0) == 2 * rowsPerChunk, "both dense region chunks have valid full extraction");
            var store = new FileSystemScanCacheStore(cache);
            var service = new WorldQueryService(inventory, store);
            var cold = await service.QueryAsync(new(scan));
            Require(cold.Snapshot is { Manifest.CoverageComplete: true } && cold.Snapshot.ReadSources().Sum(row => row.Text?.Occurrences.Length ?? 0) == 2 * rowsPerChunk,
                "aggregate region cache limits must preserve every independently bounded chunk");
            var warm = await service.QueryAsync(new(scan));
            Require(warm.Before.State == CacheState.Fresh && Rows(warm.Snapshot!).SequenceEqual(Rows(cold.Snapshot!)),
                "warm cache retains its chunk bindings, text and outcomes");
            var descriptor = cold.Snapshot!.Manifest.Artifacts.Single();
            using var index = System.Text.Json.JsonDocument.Parse(store.ReadShard(descriptor.ContentHash)!);
            var parts = index.RootElement.GetProperty("data").GetProperty("parts");
            Require(parts.GetArrayLength() >= 2, "the oversized region is stored in separately bounded shards");
            File.WriteAllText(Path.Combine(cache, parts[0].GetString() + ".shard.json"), "changed");
            Require(service.Inspect(new(scan)).State == CacheState.Corrupt, "a changed partition cannot be treated as a complete cached region");
            var repaired = await service.QueryAsync(new(scan));
            Require(repaired.Telemetry.ScannedArtifacts == 1 && Rows(repaired.Snapshot!).SequenceEqual(Rows(cold.Snapshot)), "repair reparses the invalid region without losing text");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void ShardSchemaAndCatalogBinding() => Binding().GetAwaiter().GetResult();
    private static async Task Binding()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); var descriptor = first.Snapshot!.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Function(0));
            var original = store.ReadShard(descriptor.ContentHash)!;
            var shard = ScanCacheJson.ReadShard(Encoding.UTF8.GetString(original));
            var wrong = ScanCacheJson.WriteShard(shard with { Sources = shard.Sources.Select(source => source with { Role = WorldSourceRole.PackJson }).ToImmutableArray() });
            ChangeShard(wrong);
            Require(service.Inspect(request).State == CacheState.Corrupt, "self-consistent cache hashes cannot substitute an invalid current source role binding");
            Require((await service.QueryAsync(request)).Telemetry.ScannedArtifacts == 1, "invalid catalog binding reparses only that artifact");
            ChangeShard(Encoding.UTF8.GetString(original).Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
            Require(service.Inspect(request).State == CacheState.UnsupportedSchema, "unknown shard serialization is UnsupportedSchema just like unknown manifest serialization");
            var repaired = await service.QueryAsync(request);
            Require(repaired.Before.State == CacheState.UnsupportedSchema && repaired.Telemetry.ScannedArtifacts == 1 && Rows(repaired.Snapshot!).SequenceEqual(Rows(first.Snapshot)), "unsupported shard is rebuilt while good shards and semantic results survive");
            void ChangeShard(string json)
            {
                var head = store.ReadManifest()!; var manifest = ScanCacheJson.ReadManifest(Encoding.UTF8.GetString(head)); var bytes = Encoding.UTF8.GetBytes(json); var hash = Hash256.Compute(bytes); store.WriteShard(hash, bytes);
                var changed = manifest with { Artifacts = manifest.Artifacts.Select(item => item.RelativePath == Function(0) ? item with { ContentHash = hash } : item).ToImmutableArray() };
                store.PublishManifest(Encoding.UTF8.GetBytes(ScanCacheJson.WriteManifest(changed)), Hash256.Compute(head));
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
