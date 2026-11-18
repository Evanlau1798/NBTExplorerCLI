using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using System.Text;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldReferenceTests
{
    internal const string Entities = "entities/r.0.0.mca";
    private const string Terrain = "region/r.0.0.mca";
    private const string Selector = "@e[type=minecraft:armor_stand,name=Guard]";
    private static NbtDocument EntityDocument() => new("", new NbtCompound([
        new("DataVersion", new NbtInt(3953)), new("Entities", new NbtList(NbtType.Compound, [
            new NbtCompound([new("id", new NbtString("minecraft:armor_stand")), new("UUID", new NbtIntArray([1, 2, 3, 4])), new("CustomName", new NbtString("{\"text\":\"Guard\",\"color\":\"red\"}")), new("Tags", new NbtList(NbtType.String, [new NbtString("protected")]))]),
            new NbtCompound([new("id", new NbtString("minecraft:zombie")), new("CustomName", new NbtString("{\"text\":\"Guard\"}"))])]))]));
    internal static string CreateWorld(bool duplicate = false)
    {
        var root = WorldQueryTests.CreateWorld();
        var entities = RegionFile.Read(new byte[8192]).WithChunk(0, EntityDocument(), NbtCompression.Zlib, 17);
        if (duplicate) entities = RegionFile.Read(entities).WithChunk(1, EntityDocument(), NbtCompression.GZip, 18);
        Put(root, Entities, entities);
        var sign = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3953)), new("block_entities", new NbtList(NbtType.Compound, [new NbtCompound([
            new("id", new NbtString("minecraft:sign")), new("front_text", new NbtCompound([new("messages", new NbtList(NbtType.String, [new NbtString("{\"selector\":\"" + Selector + "\"}")]))]))])]))]));
        Put(root, Terrain, RegionFile.Read(new byte[8192]).WithChunk(0, sign, NbtCompression.Zlib, 19));
        Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("execute as " + Selector + " run tellraw @s {\"text\":\"Guard\"}\n"));
        return root;
    }
    internal static LiteralTranslation[] Names(WorldQuerySnapshot snapshot, bool all = true) => snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? [])
        .Where(row => row.Address.SemanticRole == "armor_stand.name").Take(all ? 100 : 1).Select(row =>
        {
            var slot = row.ToSlot("en_us", "zh_tw"); return new LiteralTranslation(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Sentinel"));
        }).ToArray();
    public static void TypedGroupPreparesAndAppliesEveryDeclaredMember()
    {
        foreach (var duplicate in new[] { false, true })
        {
            var root = CreateWorld(duplicate); var state = ScratchDirectory.CreateChild();
            try
            {
                var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache"));
                Require(snapshot.Manifest.CoverageComplete, "fixture has complete actual extraction, including the literal-free native selector");
                var inventory = new WorldInventory(root, World); var operation = new WorldReferenceService(inventory).Create(snapshot, "rename", Names(snapshot));
                Require(operation.Synchronization.Members.Length == (duplicate ? 4 : 3), "only explicitly selected typed names plus both real references are members");
                var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
                Require(plan.Outputs.Length == 3 && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "preview declares the whole native/direct group without changing world bytes");
                using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
                Require(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "stage remains separate from all official world outputs");
                var receipt = WorldApplyTests.Engine(root, state).Apply(prepared, true);
                Require(receipt.Success && receipt.Requested == 1 && receipt.Counts[OperationOutcome.Applied] == 1, "one group accounts once after all three physical outputs read back");
                var region = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, Entities)));
                foreach (var index in duplicate ? new[] { 0, 1 } : new[] { 0 })
                {
                    var document = region.ReadChunk(index).Document;
                    var name = ((NbtString)NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")])).Value!).Value;
                    var other = ((NbtString)NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(1), PathSegment.ForKey("CustomName")])).Value!).Value;
                    Require(name == "{\"text\":\"Sentinel\",\"color\":\"red\"}" && other == "{\"text\":\"Guard\"}", "the typed selected target changes while same-spelled zombie names and style remain protected");
                    Require(NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("UUID")])).Value == new NbtIntArray([1, 2, 3, 4]), "reference synchronization preserves the selected entity's exact UUID array");
                }
                var function = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(0))));
                Require(function.Contains("name=\"Sentinel\"", StringComparison.Ordinal) && function.Contains("{\"text\":\"Guard\"}", StringComparison.Ordinal), "direct reference changes without changing identical literal text");
                Require(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(1))).SequenceEqual(before[Path.Combine(root, WorldQueryTests.Function(1)).Replace('/', Path.DirectorySeparatorChar)]), "player name selectors are separate from entity CustomName references");
                var sign = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, Terrain))).ReadChunk(0).Document;
                var encoded = ((NbtString)NbtLocator.Resolve(sign.Root, new([PathSegment.ForKey("block_entities"), PathSegment.ForIndex(0), PathSegment.ForKey("front_text"), PathSegment.ForKey("messages"), PathSegment.ForIndex(0)])).Value!).Value;
                Require(encoded.Contains("Sentinel", StringComparison.Ordinal) && !encoded.Contains("Guard", StringComparison.Ordinal), "literal-free native selectors participate in the same group");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void AmbiguousTargetsAndForgedCompleteClaimsAreRejected()
    {
        var root = CreateWorld(true); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            Reject<EditRejectedException>(() => new WorldReferenceService(inventory).Create(snapshot, "partial-targets", Names(snapshot, false)));
            var valid = new WorldReferenceService(inventory).Create(snapshot, "complete", Names(snapshot)); var group = valid.Synchronization;
            var members = group.Members.Where(member => member.Address.SemanticRole != "reference.component.selector").ToArray();
            var forged = new ReferencePatch("forged", new(group.GroupId, group.GroupRevision, SourceIdentity.Members(group.GroupId, members.Select(member => member.Occurrence), group.GroupRevision).Digest, ReferenceCompleteness.Complete, members));
            Reject<EditRejectedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [forged]));
            Require(!Directory.GetFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "missing target/reference members never reach staging");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void NewReferencesAfterPrepareCannotAuthorizeStaleWrites()
    {
        var root = CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var operation = new WorldReferenceService(inventory).Create(snapshot, "rename", Names(snapshot)); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var before = File.ReadAllBytes(Path.Combine(root, Entities)); var added = "datapacks/p/data/test/function/added.mcfunction";
            Put(root, added, Encoding.UTF8.GetBytes("execute as " + Selector + " run tellraw @s \"new\"\n"));
            Reject<ArtifactConflictException>(() => WorldApplyTests.Engine(root, state).Apply(prepared, true));
            Require(File.ReadAllBytes(Path.Combine(root, Entities)).SequenceEqual(before) && File.Exists(Path.Combine(root, added)), "new relevant catalog entries reject before the first world write and remain intact");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void DynamicAndUntypedReferenceDomainsAreBlocked()
    {
        foreach (var command in new[] { "$execute as @e[name=$(target)] run tellraw @s \"macro\"", "execute as @e[name=Guard] run tellraw @s \"untyped\"" })
        {
            var root = CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                Put(root, WorldQueryTests.Function(1), Encoding.UTF8.GetBytes(command));
                var snapshot = WorldQueryTests.Query(root, Path.Combine(state, "cache"));
                Reject<EditRejectedException>(() => new WorldReferenceService(new WorldInventory(root, World)).Create(snapshot, "unsafe", Names(snapshot)));
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void StagedPrivateCandidatesDoNotBecomeWorldSources()
    {
        var root = WorldQueryTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var first = WorldQueryTests.Query(root, Path.Combine(state, "before"));
            var artifact = new ArtifactAddress(World, WorldQueryTests.Function(0), ArtifactKind.McFunction);
            var storage = new ArtifactStorage(root, World); var bytes = storage.Read(artifact);
            using (var candidate = storage.Stage(artifact, ArtifactImage.Present(Hash256.Compute(bytes)), bytes))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                var staged = WorldQueryTests.Query(root, Path.Combine(state, "staged"));
                Require(staged.Manifest.CoverageComplete && staged.Manifest.SnapshotId == first.Manifest.SnapshotId,
                    "a real private GUID candidate and writer marker do not change semantic source coverage or snapshot identity");
            }
            Put(root, "datapacks/p/data/test/function/.nbtcli-not-a-guid.candidate", [1]);
            var lookalike = WorldQueryTests.Query(root, Path.Combine(state, "lookalike"));
            Require(!lookalike.Manifest.CoverageComplete, "a reserved-prefix lookalike is not silently whitelisted as a generated candidate");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
}
