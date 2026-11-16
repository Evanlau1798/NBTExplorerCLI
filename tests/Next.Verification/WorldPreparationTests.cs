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

internal static class WorldPreparationTests
{
    private const string Advancement = "datapacks/p/data/test/advancement/a.json";
    private const string AdvancementJson = "{\"display\":{\"title\":{\"text\":\"Title\",\"extra\":[{\"text\":\"Tail\"}],\"color\":\"red\",\"clickEvent\":{\"action\":\"run_command\",\"value\":\"Title\"}}},\"n\":1e+02}";
    private static string CreateWorld()
    {
        var root = WorldQueryTests.CreateWorld(); var level = WorldMetadataTests.Level(3953, WorldMetadataTests.Dimensions);
        var data = (NbtCompound)((NbtCompound)level.Root).Entries.Single().Value;
        var boss = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString("{\"text\":\"Hello\"}"))]))]);
        Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Concat(new NbtEntry[] { new("CustomBossEvents", boss), new("Counter", new NbtInt(7)) })))])), NbtCompression.GZip));
        Put(root, Advancement, Encoding.UTF8.GetBytes(AdvancementJson)); return root;
    }
    private static PatchOperation[] Operations(WorldQuerySnapshot snapshot)
    {
        var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.Artifact.RelativePath is "level.dat" or Advancement || item.Address.Artifact.RelativePath == WorldQueryTests.Function(0)).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
        Require(slots.Length == 4, "real DAT embedded JSON, native JSON fragments and mcfunction supply the exact four intended slots");
        var literal = slots.Select((slot, index) => (PatchOperation)new LiteralPatch("literal-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "譯:" + slot.SourceText))));
        var path = new TypedPath([PathSegment.ForKey("Data"), PathSegment.ForKey("Counter")]);
        return literal.Append(new TypedPatch("typed-counter", new(new(World, "level.dat", ArtifactKind.Nbt), path, "typed.value"), TypedEdit.Replace(path, new NbtInt(7), new NbtInt(8)))).ToArray();
    }
    private static Dictionary<string, byte[]> Originals(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private static void OriginalsUnchanged(Dictionary<string, byte[]> originals) => Require(originals.All(item => File.ReadAllBytes(item.Key).SequenceEqual(item.Value)), "preparation preserves every original file byte");
    public static void MixedCandidatesAreCompleteAndNeverCommit()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World); var originals = Originals(root);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, Operations(snapshot));
            Require(plan.Operations.Length == 5 && plan.Outputs.Length == 3 && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count() == originals.Count, "read-only preview declares one physical output per artifact without creating files");
            var service = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));
            using (var prepared = service.Prepare(snapshot, plan, worldOffline: true))
            {
                Require(prepared.Plan.PlanHash == plan.PlanHash && prepared.Manifest.PlanHash == plan.PlanHash && prepared.Manifest.Artifacts.Length == 3 && prepared.Manifest.Artifacts.All(item => File.Exists(item.CandidatePath)), "all complete physical candidates are staged/verified before one bound manifest is exposed");
                OriginalsUnchanged(originals);
                foreach (var artifact in prepared.Manifest.Artifacts)
                {
                    var bytes = File.ReadAllBytes(artifact.CandidatePath!); Require(artifact.Postimage == ArtifactImage.Present(Hash256.Compute(bytes)) && artifact.Checks.All(check => check.Passed), "each frozen candidate has exact persisted postimage and passed checks");
                    if (artifact.Artifact.RelativePath == Advancement)
                    {
                        var expected = AdvancementJson.Replace("\"text\":\"Title\"", "\"text\":\"譯:Title\"", StringComparison.Ordinal).Replace("\"text\":\"Tail\"", "\"text\":\"譯:Tail\"", StringComparison.Ordinal);
                        Require(bytes.SequenceEqual(Encoding.UTF8.GetBytes(expected)), "native JSON keeps event/style/number spelling while both exact fragments change");
                    }
                    else if (artifact.Artifact.RelativePath == "level.dat")
                    {
                        var document = CompressedNbt.ReadDocument(bytes, NbtCompression.GZip).Document;
                        Require(NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Data"), PathSegment.ForKey("Counter")])).Value == new NbtInt(8), "typed and translated operations share one DAT candidate");
                        var text = (NbtString)NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Data"), PathSegment.ForKey("CustomBossEvents"), PathSegment.ForKey("test:event"), PathSegment.ForKey("Name")])).Value!;
                        using var json = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(text.Value)); Require(NativeJsonStrings.Read(json.RootElement.GetProperty("text")) == "譯:Hello", "legacy embedded JSON uses its exact decoded component path");
                    }
                    else Require(Encoding.UTF8.GetString(bytes) == "tellraw @a[name=Guard] \"譯:Text0\"\n", "pure text command candidate keeps selector and line framing");
                }
            }
            OriginalsUnchanged(originals); Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "disposing a complete prepared set removes owned candidates and never commits");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private sealed class FailedSecondStage(IArtifactStorage inner) : IArtifactStorage
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        private int count;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
            => ++count == 2 ? throw new IOException("Injected second artifact stage failure") : inner.Stage(artifact, preimage, bytes);
    }
    private sealed class MutateMetadataInventory(IWorldInventory inner, Action change) : IWorldInventory
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        private int metadataReads;
        public Guid WorldId => inner.WorldId;
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000) => inner.Enumerate(maxEntries);
        public byte[] Read(ArtifactAddress artifact)
        {
            if (artifact.RelativePath == "level.dat" && ++metadataReads == 2) change();
            return inner.Read(artifact);
        }
    }
    private sealed class TamperEarlierCandidate(IArtifactStorage inner) : IArtifactStorage
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        private IArtifactCandidate? first;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
        {
            var candidate = inner.Stage(artifact, preimage, bytes);
            if (first is null) first = candidate;
            else File.WriteAllBytes(first.CandidatePath, [0]);
            return candidate;
        }
    }
    public static void ScopedPlansRetainInterpretationPreimages()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World);
            var scope = new SnapshotScope([], [], [], [WorldQueryTests.Function(0)]);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache)).QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
            Require(snapshot.ReadSources().All(item => item.Source.Location.Artifact.RelativePath != "level.dat"), "scoped payload omits level, making metadata retention independently observable");
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single().ToSlot("en_us", "zh_tw");
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("only-function", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated")))]);
            Require(plan.Preconditions.Select(item => item.Artifact.RelativePath).Order().SequenceEqual(new[] { "datapacks/p/pack.mcmeta", WorldQueryTests.Function(0), "level.dat" }.Order()), "scoped plan retains raw world and pack interpretation metadata as well as its selected source");
            var service = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));
            using (var prepared = service.Prepare(snapshot, plan, true)) Require(prepared.Manifest.Artifacts.Length == 1, "fresh scoped metadata permits exact staging");
            var level = CompressedNbt.ReadDocument(File.ReadAllBytes(Path.Combine(root, "level.dat")), NbtCompression.GZip).Document;
            var changed = TypedTreeEdits.Apply(level, [TypedEdit.Replace(new([PathSegment.ForKey("Data"), PathSegment.ForKey("Counter")]), new NbtInt(7), new NbtInt(9))]);
            var changedBytes = CompressedNbt.WriteDocument(changed, NbtCompression.GZip);
            var raced = new MutateMetadataInventory(inventory, () => Put(root, "level.dat", changedBytes));
            Reject<ArtifactConflictException>(() => new WorldPlanBuilder(raced).Build(snapshot, plan.Operations));
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(changedBytes), "metadata race happens after the first preimage check and keeps the same version/profile");
            Reject<ArtifactConflictException>(() => service.Prepare(snapshot, plan, true));
            Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "same-profile metadata change cannot be silently rebound to a previous plan");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void MissingAuthorityStaleSourceAndStageFailurePreserveWorld()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World); var builder = new WorldPlanBuilder(inventory); var plan = builder.Build(snapshot, Operations(snapshot)); var originals = Originals(root);
            var service = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));
            Reject<ArgumentException>(() => service.Prepare(snapshot, plan, worldOffline: false));
            Reject<IOException>(() => new WorldPlanPreparation(inventory, new FailedSecondStage(new ArtifactStorage(root, World))).Prepare(snapshot, plan, worldOffline: true));
            Reject<ArtifactConflictException>(() => { using var rejected = new WorldPlanPreparation(inventory, new TamperEarlierCandidate(new ArtifactStorage(root, World))).Prepare(snapshot, plan, worldOffline: true); });
            OriginalsUnchanged(originals); Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "failure after staging another artifact cleans the entire uncommitted set");
            var fake = new TextSlot(new(new(World, Advancement, ArtifactKind.Json), new([PathSegment.ForKey("display"), PathSegment.ForKey("title"), PathSegment.ForKey("color")]), "advancement.title"), "red", "advancement.title", "en_us", "zh_tw", false, PlaceholderPolicy.None);
            Reject<EditRejectedException>(() => builder.Build(snapshot, [new LiteralPatch("fake", new(fake, new(fake.Id, fake.SourceTextHash, "zh_tw", "blue")))]));
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("tellraw @s \"External\"\n"));
            Reject<ArtifactConflictException>(() => service.Prepare(snapshot, plan, worldOffline: true));
            Require(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(0)))) == "tellraw @s \"External\"\n" && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "a stale raw source is not rebased or overwritten and exposes no candidate set");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
