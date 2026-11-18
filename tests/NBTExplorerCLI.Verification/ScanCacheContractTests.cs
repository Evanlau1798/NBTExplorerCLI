using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class ScanCacheContractTests
{
    private static ArtifactCacheShard Shard()
    {
        var artifact = new ArtifactAddress(World, "playerdata/test.dat", ArtifactKind.Nbt);
        var address = new SourceAddress(artifact, new([PathSegment.ForKey("Command")]), "command.block.text", syntax: new("java-commands-1.21", 1, 2, 4, new string('0', 64), "utf8", "none", new([PathSegment.ForIndex(0), PathSegment.ForKey("text")])));
        var text = new TextExtractionResult([new(address, "Same\ud800", "command.block.text", true)], [], [], [], []);
        return new(Hash256.Compute([1]), [new(artifact, WorldSourceRole.Player, null, SourceOutcome.Unsupported, "UnknownReference", text,
            new(text, [new(address, "Guard\udfff", false)], ReferenceCompleteness.Unresolved))]);
    }
    private static ScanCacheManifest Manifest(string shard)
    {
        var source = Shard().Sources.Single(); var inputs = ImmutableArray.Create(new ManifestSource(source.Artifact, Hash256.Compute([2]), SourceOutcome.Success));
        var versions = new InterpretationVersions("codec1", "profile2", "rules1", "policy2");
        return new(World, SnapshotScope.All, versions, SourceIdentity.Snapshot(World, inputs, SnapshotScope.All, versions),
            [new(source.Artifact.RelativePath, Shard().ParseKey, Hash256.Compute(Encoding.UTF8.GetBytes(shard)), 1, 1, 1, false)], inputs, false);
    }
    public static void ExactRoundtripAndSnapshotBinding()
    {
        var shard = Shard(); var json = ScanCacheJson.WriteShard(shard); var decoded = ScanCacheJson.ReadShard(json);
        Require(ScanCacheJson.WriteShard(decoded) == json && decoded.ParseKey == shard.ParseKey
            && decoded.Sources.Single().Text!.Occurrences.Single().Text == "Same\ud800"
            && decoded.Sources.Single().Commands!.References.Single().Name == "Guard\udfff"
            && decoded.Sources.Single().Text!.Occurrences.Single().Address == shard.Sources.Single().Text!.Occurrences.Single().Address,
            "cache shard retains exact UTF16 text, syntax paths, source outcome and independent references");
        var manifest = Manifest(json); var manifestJson = ScanCacheJson.WriteManifest(manifest); var result = ScanCacheJson.ReadManifest(manifestJson);
        Require(ScanCacheJson.WriteManifest(result) == manifestJson && result.SnapshotId == manifest.SnapshotId && !result.CoverageComplete,
            "manifest binds scope/versions/raw source snapshot and incomplete semantic coverage separately");
        var changed = JsonNode.Parse(manifestJson)!; changed["data"]!["worldId"] = Guid.NewGuid().ToString("D");
        Reject<JsonException>(() => ScanCacheJson.ReadManifest(changed.ToJsonString()));
        Reject<NotSupportedException>(() => ScanCacheJson.ReadShard(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
    }
    public static void MalformedShardsCannotBecomeComplete()
    {
        var json = ScanCacheJson.WriteShard(Shard());
        var unknown = JsonNode.Parse(json)!; unknown["data"]!["sources"]![0]!["outcome"] = "future";
        Reject<JsonException>(() => ScanCacheJson.ReadShard(unknown.ToJsonString()));
        var missing = JsonNode.Parse(json)!; missing["data"]!["sources"]![0]!["text"]!["occurrences"] = null;
        Reject<JsonException>(() => ScanCacheJson.ReadShard(missing.ToJsonString()));
        var wrong = JsonNode.Parse(json)!; wrong["data"]!["sources"]![0]!["outcome"] = "success";
        Reject<JsonException>(() => ScanCacheJson.ReadShard(wrong.ToJsonString()));
        var irrelevant = Shard() with { Sources = Shard().Sources.Select(source => source with { Outcome = SourceOutcome.Irrelevant }).ToImmutableArray() };
        Reject<JsonException>(() => ScanCacheJson.WriteShard(irrelevant));
        var missingReferences = Shard() with { Sources = Shard().Sources.Select(source => source with { Text = null, Commands = source.Commands! with { References = default } }).ToImmutableArray() };
        Reject<JsonException>(() => ScanCacheJson.WriteShard(missingReferences));
        var unsafePath = Manifest(json) with { Artifacts = [new("../outside", Hash256.Compute([1]), Hash256.Compute([2]), 1, 1, 1, false)] };
        Reject<JsonException>(() => ScanCacheJson.ReadManifest(ScanCacheJson.WriteManifest(unsafePath)));
    }
}
