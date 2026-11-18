using System.Collections.Concurrent;
using System.Diagnostics;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class PreparationWorkTests
{
    private sealed record Work(string Operation, string? Artifact, string? Phase, long Bytes, long Comparisons, string? World);

    public static void ArchiveWorkIncludesEntryDecodeAndPersistedValidation(string? artifactRoot)
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string relative = "resources.zip";
            const string member = "assets/test/lang/en_us.json";
            Put(root, relative, Zip(("pack.mcmeta", ResourcePack), (member, "{\"key\":\"Before\"}")));
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? [])
                .Single(item => item.Address.Artifact.ContainerEntry == member).ToSlot("en_us", "zh_tw");
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("zip-work", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "After")))]);
            var preparation = new WorldPlanPreparation(inventory, storage);
            byte[] expected;
            using (var control = preparation.Prepare(snapshot, plan, true)) expected = File.ReadAllBytes(control.Manifest.Artifacts.Single().CandidatePath!);
            var rows = new ConcurrentQueue<Work>(); using var listener = Observe(rows);
            using var prepared = preparation.Prepare(snapshot, plan, true);
            Require(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!).SequenceEqual(expected), "archive observation preserves the complete candidate including metadata and opaque members");
            Require(rows.Any(row => row.Artifact == relative && row.Phase == "staging" && row.Operation == "zip.entry.read" && row.Bytes == 16)
                && rows.Any(row => row.Artifact == relative && row.Phase == "readback" && row.Operation == "zip.validate" && row.Bytes > 0),
                "archive entry decoded bytes and persisted whole-archive validation are independently observable");
            Require(rows.Any(row => row.Artifact == relative && row.Phase == "projection" && row.Operation == "json.parse"),
                "in-memory entry projection validation remains separate from physical archive readback");
            Save(artifactRoot, "archive", rows);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    public static void ResourceRenderingKeepsEachPhysicalOutputIdentity(string? artifactRoot)
    {
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                if (format == ResourceOutputFormat.ZipPack)
                    Put(root, "Pack.zip", Zip(("pack.mcmeta", ResourcePack), ("assets/minecraft/lang/en_us.json", "{\"key\":\"Before\"}"), ("assets/test/opaque.bin", "retained")));
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
                var service = new ResourceOutputService(inventory, storage, new FileSystemWorldBackupStore(state));
                var request = new ResourceGeneration(World, format, format == ResourceOutputFormat.ZipPack ? "Pack.zip" : "Pack",
                    [new("en_us", [new("key", "Before")]), new("zh_tw", [new("key", "After")])], format == ResourceOutputFormat.LanguageFiles ? null : new(34, 0));
                var plan = service.Build(request);
                Dictionary<string, byte[]> expected;
                using (var control = service.Prepare(plan, true)) expected = control.Manifest.Artifacts.ToDictionary(item => item.Artifact.RelativePath, item => File.ReadAllBytes(item.CandidatePath!));
                var rows = new ConcurrentQueue<Work>(); using var listener = Observe(rows);
                using var prepared = service.Prepare(plan, true);
                Require(prepared.Manifest.Artifacts.Length == request.Outputs.Length && prepared.Manifest.Artifacts.All(item => File.ReadAllBytes(item.CandidatePath!).SequenceEqual(expected[item.Artifact.RelativePath])),
                    "resource observation preserves all language and metadata candidate bytes");
                foreach (var artifact in prepared.Manifest.Artifacts)
                    Require(rows.Any(row => row.Artifact == artifact.Artifact.RelativePath && row.World == World.ToString("D")
                        && row.Phase == "render" && row.Operation == (format == ResourceOutputFormat.ZipPack ? "resource.archive" : "resource.render")
                        && row.Bytes == expected[artifact.Artifact.RelativePath].Length),
                        "render bytes belong to the actual declared physical output and world before staging");
                Require(rows.Where(row => row.Operation == "language.add").Sum(row => row.Comparisons) == 2,
                    "language rendering records one indexed existing-key check per requested entry");
                if (format == ResourceOutputFormat.ZipPack)
                    Require(rows.Any(row => row.Operation == "zip.validate" && row.Phase == "bind" && row.Artifact == "Pack.zip" && row.World == World.ToString("D")),
                        "existing physical output ZIP validation retains its identity during binding");
                Save(artifactRoot, "resource-" + format, rows);
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }

    public static void MixedCodecsRecordActualDecodedWork(string? artifactRoot)
    {
        var root = WorldPreparationTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? [])
                .Where(item => item.Address.Artifact.RelativePath is "level.dat" or "datapacks/p/data/test/advancement/a.json"
                    || item.Address.Artifact.RelativePath == WorldQueryTests.Function(0)).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
            Require(slots.Length == 4, "mixed observation includes all four declared native/encoded/function slots");
            var operations = slots.Select((slot, index) => new LiteralPatch("mixed-work-" + index,
                new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "observed " + slot.SourceText))));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            var preparation = new WorldPlanPreparation(inventory, storage);
            Dictionary<string, byte[]> expected;
            using (var control = preparation.Prepare(snapshot, plan, true))
                expected = control.Manifest.Artifacts.ToDictionary(item => item.Artifact.RelativePath, item => File.ReadAllBytes(item.CandidatePath!));
            var rows = new ConcurrentQueue<Work>();
            using var listener = Observe(rows);
            using var prepared = preparation.Prepare(snapshot, plan, true);
            Require(prepared.Manifest.Artifacts.All(item => File.ReadAllBytes(item.CandidatePath!).SequenceEqual(expected[item.Artifact.RelativePath])),
                "mixed observation leaves all canonical staged outputs byte-identical");
            foreach (var artifact in new[] { "level.dat", "datapacks/p/data/test/advancement/a.json" })
                Require(rows.Any(row => row.Artifact == artifact && row.Phase == "staging" && row.Operation == "json.spans" && row.Bytes > 0)
                    && rows.Any(row => row.Artifact == artifact && row.Phase == "staging" && row.Operation == "json.rewrite" && row.Bytes > 0 && row.Comparisons > 0),
                    "native and encoded JSON expose actual full-source parse, decoded output bytes and checked candidate counts");
            Require(rows.Any(row => row.Artifact == "level.dat" && row.Phase == "staging" && row.Operation == "nbt.read" && row.Bytes > 0)
                && rows.Any(row => row.Artifact == "level.dat" && row.Phase == "staging" && row.Operation == "nbt.write" && row.Bytes > 0)
                && rows.Any(row => row.Artifact == "datapacks/p/data/test/advancement/a.json" && row.Phase == "readback" && row.Operation == "json.parse" && row.Bytes > 0),
                "compressed NBT decoded reads/writes and persisted JSON parse remain separate observations");
            Save(artifactRoot, "mixed", rows);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    public static void FunctionWorkIncludesBindingAndPhysicalIo(string? artifactRoot)
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var storage = new ArtifactStorage(root, World);
            using var control = WorldApplyTests.Functions(root, state, storage);
            var expected = control.Manifest.Artifacts.ToDictionary(item => item.Artifact.RelativePath,
                item => File.ReadAllBytes(item.CandidatePath!));
            var rows = new ConcurrentQueue<Work>();
            using var listener = Observe(rows);
            using var prepared = WorldApplyTests.Functions(root, state, storage);
            Require(PatchPlanJson.Write(prepared.Plan) == PatchPlanJson.Write(control.Plan)
                && prepared.Manifest.Artifacts.All(item => File.ReadAllBytes(item.CandidatePath!).SequenceEqual(expected[item.Artifact.RelativePath])),
                "observed function preparation preserves canonical plan and every staged byte");
            foreach (var artifact in prepared.Manifest.Artifacts)
            {
                var selected = rows.Where(row => row.Artifact == artifact.Artifact.RelativePath).ToArray();
                Require(selected.All(row => row.World == World.ToString("D")), "work records retain the exact world identity of their physical artifact");
                Require(selected.Any(row => row.Operation == "candidate.lookup" && row.Phase == "bind" && row.Comparisons == 1),
                    "binding records the actual indexed candidate lookup for each selected function");
                Require(selected.Count(row => row.Operation == "command.parse" && row.Phase == "staging") == 1
                    && selected.Count(row => row.Operation == "command.parse" && row.Phase == "projection") == 2
                    && selected.Count(row => row.Operation == "command.parse" && row.Phase == "readback") == 2,
                    "source parsing, in-memory projection checks and persisted readback checks remain separately observable");
                Require(selected.Where(row => row.Operation == "candidate.write").Sum(row => row.Bytes) == expected[artifact.Artifact.RelativePath].Length
                    && selected.Any(row => row.Operation == "storage.read" && row.Bytes > 0)
                    && selected.Count(row => row.Operation == "candidate.verify" && row.Bytes > 0 && row.Comparisons == 1) == 2,
                    "candidate bytes written, source payloads read and both real hash comparisons are observed");
            }
            Save(artifactRoot, "functions", rows);
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }

    private static ActivityListener Observe(ConcurrentQueue<Work> rows)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "nbtcli.work",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                string? Inherited(string tag)
                {
                    for (var parent = activity; parent is not null; parent = parent.Parent)
                        if (parent.GetTagItem(tag) is string value) return value;
                    return null;
                }
                rows.Enqueue(new(activity.OperationName, Inherited("artifact"), Inherited("phase"),
                    activity.GetTagItem("bytes") is long bytes ? bytes : 0,
                    activity.GetTagItem("comparisons") is long comparisons ? comparisons : 0, Inherited("world")));
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static void Save(string? root, string name, ConcurrentQueue<Work> rows)
    {
        if (root is null) return;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "work-" + name + ".json"), System.Text.Json.JsonSerializer.Serialize(rows.ToArray()));
    }

    public static void RegionCountsSeparateStagingFromReadback(string? artifactRoot)
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string relative = "region/r.0.0.mca";
            var original = new byte[5 * 4096];
            for (var index = 0; index < 3; index++) RegionBatchPreparationTests.WriteChunk(original, index, index + 2, 200000 + index);
            Put(root, relative, original);
            var inventory = new WorldInventory(root, World);
            var snapshot = WorldQueryTests.Query(root, cache);
            var artifact = new ArtifactAddress(World, relative, ArtifactKind.Region);
            var path = new TypedPath([PathSegment.ForKey("Counter")]);
            var operations = Enumerable.Range(0, 2).Select(index => new TypedPatch("work-" + index,
                new SourceAddress(artifact, path, "typed.value", "minecraft:overworld", new(index, 0)),
                TypedEdit.Replace(path, new NbtInt(200000 + index), new NbtInt(1200000 + index)))).ToArray();
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            var preparation = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));
            byte[] expected;
            using (var control = preparation.Prepare(snapshot, plan, true)) expected = File.ReadAllBytes(control.Manifest.Artifacts.Single().CandidatePath!);
            var rows = new ConcurrentQueue<Work>();
            using var listener = Observe(rows);
            using var prepared = preparation.Prepare(snapshot, plan, true);
            Require(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!).SequenceEqual(expected)
                && File.ReadAllBytes(Path.Combine(root, relative)).SequenceEqual(original), "enabled work observation preserves exact candidate bytes and every original sector");
            var staging = rows.Where(row => row.Artifact == relative && row.Phase == "staging").ToArray();
            Require(rows.Count(row => row.Artifact == relative && row.Phase == "discovery" && row.Operation == "region.parse") == 1
                && rows.Any(row => row.Artifact == "level.dat" && row.Phase == "discovery" && row.Operation == "nbt.read" && row.Bytes > 0),
                "discovery index and metadata parses retain their own physical artifact identity outside staging");
            Require(!rows.Any(row => row.Artifact == relative && row.Phase == "bind" && row.Operation == "artifact.extract"),
                "typed-only binding validates fresh source metadata and selected typed chunks without decoding unrelated text sources");
            Require(staging.Count(row => row.Operation == "region.parse") == 1, "one physical region index is parsed once during candidate construction");
            Require(staging.Count(row => row.Operation == "region.chunk.load") == 2, "only the two selected chunk documents load during staging");
            Require(staging.Where(row => row.Operation == "nbt.read").Sum(row => row.Bytes) > 0
                && staging.Where(row => row.Operation == "nbt.write").Sum(row => row.Bytes) > 0, "decoded parse and serialization bytes are recorded from actual codec results");
            Require(rows.Any(row => row.Artifact == relative && row.Phase == "readback" && row.Operation == "region.parse")
                && rows.Where(row => row.Artifact == relative && row.Operation == "region.compare").Sum(row => row.Comparisons) == 1024,
                "persisted candidate parsing and all descriptor comparisons remain separate from staging");
            Save(artifactRoot, "region", rows);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
