using System.Text;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldQueryTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldQueryFailureTests
{
    public static void FileSetFiltersAndInterpretations() => Changes().GetAwaiter().GetResult();
    private static async Task Changes()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            await service.QueryAsync(request);
            var added = Path.Combine(root, Function(3)); File.WriteAllText(added, "tellraw @s \"Added\"\n", new UTF8Encoding(false));
            Require(service.Inspect(request) == new CacheInspection(CacheState.Dirty, "SourceSetChanged"), "new file changes the verified source set");
            Require((await service.QueryAsync(request)).Telemetry.ScannedArtifacts == 1, "new artifact parses without repeating unchanged groups");
            File.Delete(Path.Combine(root, Function(1)));
            var deleted = await service.QueryAsync(request);
            Require(deleted.Before.State == CacheState.Dirty && deleted.Telemetry.ScannedArtifacts == 0 && deleted.Snapshot!.ReadSources().All(source => source.Source.Location.Artifact.RelativePath != Function(1)), "deleted artifact leaves the manifest without parsing unchanged artifacts");
            var renamed = "datapacks/p/data/test/function/renamed.mcfunction"; File.Move(Path.Combine(root, Function(2)), Path.Combine(root, renamed));
            Require((await service.QueryAsync(request)).Telemetry.ScannedArtifacts == 1, "renamed artifact has a new real identity and parse binding");
            var filteredRequest = request with { Scan = new(new([], [], ["command.function"], [Function(0)])) };
            Require(service.Inspect(filteredRequest) == new CacheInspection(CacheState.Dirty, "FilterChanged"), "scope identity is checked independently from content");
            var filtered = await service.QueryAsync(filteredRequest);
            Require(filtered.Snapshot!.ReadSources().Single().Source.Location.Artifact.RelativePath == Function(0), "all consumers observe actual scope adapter output");
            var raw = store.ReadManifest()!; var manifest = ScanCacheJson.ReadManifest(Encoding.UTF8.GetString(raw)); var versions = manifest.Versions with { Codec = "codec-previous" };
            var changed = manifest with { Versions = versions, SnapshotId = SourceIdentity.Snapshot(World, manifest.InputSources, manifest.Scope, versions) };
            store.PublishManifest(Encoding.UTF8.GetBytes(ScanCacheJson.WriteManifest(changed)), Hash256.Compute(raw));
            Require(service.Inspect(filteredRequest) == new CacheInspection(CacheState.Dirty, "InterpretationChanged"), "known codec/profile/rules/policy revision changes are dirty, not unsupported serialization");
            Require((await service.QueryAsync(filteredRequest)).Telemetry.ScannedArtifacts == 1, "an interpretation change reparses its affected artifact even if stored descriptors claim current parse keys");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void CorruptShardAndUnknownSchemaRecovery() => Corrupt().GetAwaiter().GetResult();
    private static async Task Corrupt()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store); var request = new WorldQueryRequest(new(SnapshotScope.All));
            var first = await service.QueryAsync(request); var expected = Rows(first.Snapshot!); var manifest = first.Snapshot!.Manifest;
            var descriptor = manifest.Artifacts.Single(artifact => artifact.RelativePath == Function(0));
            File.WriteAllBytes(Path.Combine(cache, descriptor.ContentHash.Hex + ".shard.json"), [1, 2, 3]);
            Require(service.Inspect(request).State == CacheState.Corrupt, "bad shard hash is a corrupt cache even while original source remains valid");
            var rebuilt = await service.QueryAsync(request);
            Require(rebuilt.Before.State == CacheState.Corrupt && rebuilt.Telemetry.Rebuilds == 1 && rebuilt.Telemetry.ScannedArtifacts == 1 && rebuilt.Telemetry.ReusedArtifacts > 0
                && Rows(rebuilt.Snapshot!).SequenceEqual(expected), "bad shard is rebuilt, good shards reused, and semantic diagnostics/results remain identical");
            var unknown = JsonNode.Parse(Encoding.UTF8.GetString(store.ReadManifest()!))!; unknown["schemaVersion"] = 2;
            File.WriteAllText(Path.Combine(cache, "manifest.json"), unknown.ToJsonString(), new UTF8Encoding(false));
            Require(service.Inspect(request).State == CacheState.UnsupportedSchema, "unknown serialization schema remains distinct from a dirty interpretation version");
            var repaired = await service.QueryAsync(request);
            Require(repaired.Before.State == CacheState.UnsupportedSchema && repaired.Telemetry.Rebuilds == 1 && Rows(repaired.Snapshot!).SequenceEqual(expected), "derived unknown-schema cache can be rebuilt through the current validated pipeline");
            File.WriteAllBytes(Path.Combine(cache, "manifest.json"), [1, 2, 3]);
            Require(service.Inspect(request).State == CacheState.Corrupt, "invalid manifest bytes are not schema-version errors");
            Require(Rows((await service.QueryAsync(request)).Snapshot!).SequenceEqual(expected), "bad manifest rebuild preserves semantic coverage and diagnostics");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void MetadataDependencyOutsideScopeAndToolState() => Dependencies().GetAwaiter().GetResult();
    private static async Task Dependencies()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            File.WriteAllText(Path.Combine(root, Function(0)), "tellraw @s {\"text\":\"Main\",\"hover_event\":{\"action\":\"show_text\",\"value\":{\"text\":\"Hover\"}}}", new UTF8Encoding(false));
            var store = new FileSystemScanCacheStore(cache); var service = new WorldQueryService(new WorldInventory(root, World), store);
            var request = new WorldQueryRequest(new(new([], [], [], [Function(0)]))); var first = await service.QueryAsync(request);
            Require(first.Snapshot!.ReadSources().Single().Text!.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Main" }), "old metadata schema is an explicit positive control");
            File.WriteAllText(Path.Combine(root, "datapacks/p/pack.mcmeta"), "{\"pack\":{\"pack_format\":71,\"description\":\"New\"}}", new UTF8Encoding(false));
            Require(service.Inspect(request).State == CacheState.Dirty, "pack dependency outside exact artifact scope invalidates interpretation");
            var changed = await service.QueryAsync(request);
            Require(changed.Snapshot!.Manifest.SnapshotId != first.Snapshot.Manifest.SnapshotId && changed.Snapshot.ReadSources().Single().Text!.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Main", "Hover" }),
                "metadata dependency changes both semantic output and snapshot identity even when selected raw artifact is unchanged");
            using (var candidate = new NBTExplorerCLI.Infrastructure.ArtifactStorage(root, World).Stage(new(World, "probe.bin", ArtifactKind.Text), ArtifactImage.Absent, new byte[] { 1 })) { }
            Require(service.Inspect(request).State == CacheState.Fresh, "internal writer marker never becomes a content-hash dependency or false dirty source");
            var warm = await service.QueryAsync(request);
            Require(warm.Snapshot!.Manifest.InputSources.All(source => source.Artifact.RelativePath != ".nbtcli-writer.lock") && warm.Snapshot.ReadSources().All(source => source.Source.DiagnosticCode != "InternalWriterLease"), "shared query excludes internal tool state from raw source manifest and semantic cache rows");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
