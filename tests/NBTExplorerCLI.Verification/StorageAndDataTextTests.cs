using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageAndDataTextTests
{
    private static TypedPath StoragePath(params string[] keys) => new(new[] { "data", "contents" }.Concat(keys).Select(PathSegment.ForKey));
    public static void ExplicitStorageRulesAndUnknownCandidates()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var contents = C(E("golden:test", C(E("value", S("Literal")), E("message", Text("Component", modern)), E("internal", S("Unknown")), E("number", new NbtInt(7)))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", C(E("contents", contents)))));
            var source = new WorldSourceLocation(new(World, modern ? "data/minecraft/command_storage_golden.dat" : "data/command_storage_golden.dat", ArtifactKind.Nbt), WorldSourceRole.SavedData);
            var unknown = NbtTextExtractor.Extract(source, document);
            Require(!unknown.Complete && unknown.Occurrences.IsEmpty && unknown.Unclassified.Length == 3
                && unknown.Unclassified.All(candidate => candidate.Address.Artifact == source.Artifact && candidate.Address.SemanticRole == "command_storage.candidate"), "arbitrary storage strings are exact unclassified candidates, never automatically authorized slots");
            StorageTextRule[] rules = [new(StoragePath("golden:test", "value"), StorageTextKind.Literal), new(StoragePath("golden:test", "message"), StorageTextKind.TextComponent)];
            var partial = NbtTextExtractor.Extract(source, document, storageRules: rules);
            var component = modern ? "/data/contents/golden:test/message/text|<physical>|" : "/data/contents/golden:test/message|/text|";
            Require(!partial.Complete && partial.Unclassified.Single().Text == "Unknown" && partial.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/data/contents/golden:test/value|<physical>|command_storage.literal|Literal", component + "command_storage.component|Component" }.Order(StringComparer.Ordinal)), "only exact explicit rules authorize literal/value or component paths; unknowns remain separate");
            var complete = NbtTextExtractor.Extract(source, document, storageRules: [.. rules, new(StoragePath("golden:test", "internal"), StorageTextKind.Literal)]);
            Require(complete.Complete && complete.Unclassified.IsEmpty && complete.Occurrences.Length == 3, "complete explicit rule coverage can resolve storage candidates");
            Reject<ArgumentException>(() => NbtTextExtractor.Extract(source, document, storageRules: [new(TypedPath.Root, StorageTextKind.Literal)]));
            Reject<ArgumentException>(() => NbtTextExtractor.Extract(source, document, storageRules: [new(StoragePath("golden:test"), StorageTextKind.TextComponent), rules[0]]));
        }
    }
    public static void NamespacedDataJsonKeepsLiteralArtifact()
    {
        var world = WorldMetadata.Read(WorldMetadataTests.Level(4774), WorldMetadataTests.Separate(4774, WorldMetadataTests.Dimensions));
        var source = new WorldSourceLocation(new(World, "data/minecraft/bossbars/example.json", ArtifactKind.Json), WorldSourceRole.SavedData);
        var bytes = Encoding.UTF8.GetBytes("{\"Name\":{\"text\":\"Boss\",\"color\":\"red\"},\"Value\":10,\"Players\":[\"Protected\"],\"value\":\"Protected\"}");
        var result = DataTextExtractor.Json(source, bytes, world);
        Require(result.Complete && Row(result.Occurrences.Single()) == "/Name/text|<physical>|bossbar.name|Boss" && result.Occurrences.Single().Address.Artifact == source.Artifact,
            "namespaced data JSON points to actual path once and authorizes only known Name component");
        var wrong = source with { Artifact = new(World, "data/other/data/minecraft/bossbars/example.json", ArtifactKind.Json) };
        var invalid = DataTextExtractor.Json(wrong, bytes, world);
        Require(!invalid.Complete && invalid.Occurrences.IsEmpty, "embedded path segment cannot impersonate declared world-data root");
    }
}
