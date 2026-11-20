using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageRetainedSeedTests
{
    internal static void OnlyCompleteUseProofCanRetain()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string seed = "data/command_storage_minecraft.dat";
            const string function = "datapacks/p/data/demo/functions/read.mcfunction";
            var message = new NbtCompound([new("message", new NbtString("""{"text":"Stored prose","bold":true}"""))]);
            var dormant = new NbtCompound([new("dormant", message)]);
            var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3465)),
                new("data", new NbtCompound([new("contents", dormant)]))]));
            var bytes = CompressedNbt.WriteDocument(document, NbtCompression.GZip);
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
            Put(root, seed, bytes);
            var inventory = new WorldInventory(root, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var initial = Query(SnapshotScope.All);
            Require(initial.Manifest.CoverageComplete && Seed(initial).Outcome == SourceOutcome.Irrelevant,
                "a complete world with no consumer can retain an unreferenced storage identifier");
            Require(Seed(initial).Text is { Occurrences.IsEmpty: true, Unclassified.IsEmpty: true },
                "an unreferenced saved string never becomes an editable slot");
            var scoped = Query(new([], [], [], [seed]));
            Require(!scoped.Manifest.CoverageComplete && Seed(scoped).Text!.Unclassified.Length == 1,
                "an artifact-only selection cannot prove that saved data is unused");

            foreach (var command in new[]
            {
                "data get storage dormant message",
                """tellraw @a {"storage":"minecraft:dormant","nbt":"message"}""",
                """tellraw @a {"block":"0 64 0","nbt":"message","interpret":true}""",
            })
            {
                Put(root, function, Encoding.UTF8.GetBytes(command));
                var blocked = Query(SnapshotScope.All);
                Require(!blocked.Manifest.CoverageComplete && Seed(blocked).Text!.Unclassified.Length == 1,
                    "an explicit alias or unresolved source removes the retention proof even when the seed hash is unchanged");
            }
            Require(File.ReadAllBytes(Path.Combine(root, seed)).SequenceEqual(bytes), "classification never rewrites the seed");
            var source = Seed(initial).Source.Location;
            var prefix = new TypedPath(new[] { "data", "contents", "dormant" }.Select(PathSegment.ForKey));
            Reject<ArgumentException>(() => NbtTextExtractor.Extract(source, document, storageRules: [new(prefix, (StorageTextKind)3)]));

            WorldQuerySnapshot Query(SnapshotScope scope) => query.QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
            TextSourceExtraction Seed(WorldQuerySnapshot snapshot) => snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == seed);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
