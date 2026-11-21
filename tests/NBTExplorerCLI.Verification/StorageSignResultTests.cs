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

internal static class StorageSignResultTests
{
    internal static readonly VerificationCase[] IndirectCases =
    [
        new("prepare-sign-relayed-result", "preparation", _ => RelayedSignResult()),
        new("prepare-sign-typed-result-guard", "preparation", _ => NativeSignOverride(reference: false)),
        new("prepare-sign-reference-result-guard", "preparation", _ => NativeSignOverride(reference: true)),
        new("prepare-sign-classic-comparator", "preparation", _ => ClassicComparator()),
        new("command-sign-native-descendant-result", "discovery", _ => NativeDescendantResult()),
    ];

    private const string Folder = "datapacks/p/data/proof/functions/";
    private const string Writer = Folder + "write.mcfunction";
    private const string Reader = Folder + "read.mcfunction";
    private const string Set = "data modify block 0 64 0 front_text.messages[1] set value ";
    private const string Read = "execute store result score $length proof run data get block 0 64 0 ";

    private static void NativeDescendantResult()
    {
        var address = new SourceAddress(new(World, "reader.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(4325)!;
        CommandExtractionResult Parse(string path) => CommandTextExtractor.Extract(
            new(address, "execute store result score #length proof run data get block 0 64 0 " + path), profile, runtimeDataVersion: 4325);
        Require(!Parse("front_text.messages[0].selector").Complete
            && !Parse("\"front_text\".\"filtered_messages\"[0].extra[0].text").Complete
            && Parse("front_text.\"messages[0].text\"").Complete,
            "raw reads of native sign descendants are guarded without misclassifying literal dotted keys");
    }

    private static void RelayedSignResult()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Initialize(world);
            Put(world, Writer, Encoding.UTF8.GetBytes(Set + "'{\"text\":\"Volcanic\"}'"));
            Put(world, Reader, "data modify storage proof:relay payload.label set from block 0 64 0 front_text.messages[1]"u8.ToArray());
            var inventory = new WorldInventory(world, World);
            var snapshot = WorldQueryTests.Query(world, cache);
            var text = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Text == "Volcanic");
            var slot = text.ToSlot("en_us", "zh_tw");
            var patch = new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山")));
            var observer = Folder + "observe.mcfunction";
            var builder = new WorldPlanBuilder(inventory);
            Put(world, observer, """execute store result score #length proof run data get storage proof:relay unrelated"""u8.ToArray());
            _ = builder.Build(snapshot, [patch]);
            Put(world, observer, "execute store result score #length proof run data get storage proof:relay payload.\"label\""u8.ToArray());
            Reject<EditRejectedException>(() => builder.Build(snapshot, [patch]));
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    private static void NativeSignOverride(bool reference)
    {
        var world = WorldReferenceTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(world, World);
            var observer = WorldQueryTests.Function(5);
            Put(world, observer, """
                data modify storage proof:relay label set from block 0 64 0 front_text.messages[0]
                execute store result score #length proof run data get storage proof:relay label
                """u8.ToArray());
            var snapshot = WorldQueryTests.Query(world, cache);
            var terrain = snapshot.ReadSources().Select(row => row.Source).Single(source => source.Chunk == new ChunkPosition(0, 0)
                && source.Location.Artifact.RelativePath == "region/r.0.0.mca");
            var path = new TypedPath([PathSegment.ForKey("block_entities"), PathSegment.ForIndex(0), PathSegment.ForKey("front_text"),
                PathSegment.ForKey("messages"), PathSegment.ForIndex(0)]);
            var doc = RegionFile.Read(inventory.Read(terrain.Location.Artifact)).ReadChunk(0).Document;
            var before = NbtLocator.Resolve(doc.Root, path).Value!;
            var address = new SourceAddress(terrain.Location.Artifact, path, "query.node", terrain.Location.Dimension, terrain.Chunk);
            PatchOperation patch = reference ? new WorldReferenceService(inventory).Create(snapshot, "rename", WorldReferenceTests.Names(snapshot))
                : new TypedPatch("raw", address, TypedEdit.Replace(path, before, new NbtString("{\"text\":\"火山\"}")));
            Reject<EditRejectedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [patch]));
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    private static void ClassicComparator()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Classic(), NbtCompression.GZip));
            var sign = new NbtCompound([new("id", new NbtString("Sign")), new("Text1", new NbtString("{\"text\":\"Volcanic\"}"))]);
            var command = new NbtCompound([new("id", new NbtString("Control")), new("Command", new NbtString(
                """testforblock 0 64 0 minecraft:standing_sign -1 {Text1:"{\"text\":\"Volcanic\"}"}"""))]);
            var level = new NbtCompound([new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)), new("TileEntities", new NbtList(NbtType.Compound, [sign, command]))]);
            var chunk = new NbtDocument("", new NbtCompound([new("Level", level)]));
            Put(world, "region/r.0.0.mca", RegionFile.Read(new byte[8192]).WithChunk(0, chunk, NbtCompression.Zlib, 1));
            var inventory = new WorldInventory(world, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var snapshot = query.QueryAsync(new(new(SnapshotScope.All, ClassicVersion: "1.8.1"))).GetAwaiter().GetResult().Snapshot!;
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? [])
                .Single(item => item.Text == "Volcanic").ToSlot("en_us", "zh_tw");
            var patch = new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山")));
            Reject<EditRejectedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [patch]));
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    internal static void ObservedResultCannotRebind()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            Initialize(world);
            const string original = Set + "'{\"text\":\"Volcanic \",\"italic\":false,\"bold\":true}'";
            Put(world, Writer, Encoding.UTF8.GetBytes(original));
            var inventory = new WorldInventory(world, World);
            var storage = new ArtifactStorage(world, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var snapshot = query.QueryAsync(new(new(new SnapshotScope([], [], [], [Writer])))).GetAwaiter().GetResult().Snapshot!;
            var slot = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == "Volcanic ").ToSlot("en_us", "zh_tw");
            var patch = new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山 ")));
            var builder = new WorldPlanBuilder(inventory);
            var plan = builder.Build(snapshot, [patch]);
            using (var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true))
            {
                Put(world, Reader, Encoding.UTF8.GetBytes(Read + "front_text.messages[1]"));
                Reject<ArtifactConflictException>(() => WorldApplyTests.Engine(world, state, storage).Apply(prepared, true));
            }
            foreach (var path in new[] { "front_text.messages[1]", "\"front_text\".\"messages\"[1]", "front_text.filtered_messages[-1]" })
            {
                Put(world, Reader, Encoding.UTF8.GetBytes(Read + path + "\nscoreboard players operation $observed proof = $length proof"));
                Reject<EditRejectedException>(() => builder.Build(snapshot, [patch]));
            }
            Require(File.ReadAllText(Path.Combine(world, Writer)) == original, "a raw sign result prevents the length-changing edit");
            Put(world, Reader, Encoding.UTF8.GetBytes(Read.Replace("store result", "store success", StringComparison.Ordinal) + "front_text.messages[1]"));
            _ = builder.Build(snapshot, [patch]);
            Put(world, Reader, Encoding.UTF8.GetBytes(Read + "front_text.messages"));
            _ = builder.Build(snapshot, [patch]);
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void IdenticalStylesProjection()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Initialize(world);
            Put(world, Writer, """data modify storage proof:panel title set value '{"text":"Bienvenue"}'"""u8.ToArray());
            const string wrapper = Set + "'[{\"nbt\":\"title\",\"storage\":\"proof:panel\",\"interpret\":true,\"italic\":false,\"italic\":false,\"bold\":true}]'";
            Put(world, Reader, Encoding.UTF8.GetBytes(wrapper));
            var snapshot = WorldQueryTests.Query(world, cache);
            Require(snapshot.Manifest.CoverageComplete, "identical repeated styles still report the exact underlying display source");
            var text = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences.Single();
            Require(text.Text == "Bienvenue", "only the actual display source is offered for translation");
            var reader = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == Reader);
            Require(reader.Text!.Occurrences.IsEmpty && File.ReadAllText(Path.Combine(world, Reader)) == wrapper,
                "the duplicate style command is preserved and does not become an editable literal");

            foreach (var unsupported in new[]
            {
                wrapper.Replace("\"italic\":false,\"italic\":false", "\"italic\":true,\"italic\":false", StringComparison.Ordinal),
                wrapper.Replace("\"nbt\":\"title\"", "\"nbt\":\"title\",\"nbt\":\"title\"", StringComparison.Ordinal),
                wrapper.Replace("\"interpret\":true", "\"interpret\":false", StringComparison.Ordinal),
                "execute store success score $changed proof run " + wrapper,
                wrapper + "\n" + Read + "front_text.messages[1]"
            })
            {
                Put(world, Reader, Encoding.UTF8.GetBytes(unsupported));
                Require(!WorldQueryTests.Query(world, cache).Manifest.CoverageComplete, "control ambiguity or observed sign values cannot authorize edits");
            }
            Put(world, Reader, Encoding.UTF8.GetBytes(wrapper + "\nexecute store result score $length proof run function proof:value"));
            Put(world, Folder + "value.mcfunction", "data get block 0 64 0 front_text.messages[1]"u8.ToArray());
            Require(!WorldQueryTests.Query(world, cache).Manifest.CoverageComplete, "a reachable result consumer also blocks the storage producer");
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    internal static void ObservedFunctionAndQuotedPaths(bool quotedKey)
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string caller = Folder + "caller.mcfunction";
        const string relay = Folder + "relay.mcfunction";
        const string getter = "data get block 0 64 0 ";
        const string call = "execute store result score $length proof run function proof:relay";
        try
        {
            Initialize(world);
            Put(world, Writer, Encoding.UTF8.GetBytes(Set + "'{\"text\":\"Volcanic \"}'"));
            var inventory = new WorldInventory(world, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var builder = new WorldPlanBuilder(inventory);
            var snapshot = query.QueryAsync(new(new(new SnapshotScope([], [], [], [Writer])))).GetAwaiter().GetResult().Snapshot!;
            var slot = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences.Single().ToSlot("en_us", "zh_tw");
            var patch = new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山 ")));
            if (quotedKey)
            {
                Put(world, Reader, Encoding.UTF8.GetBytes(Read + "front_text.\"messages[1]\""));
                _ = builder.Build(snapshot, [patch]);
                Put(world, Reader, """execute store result score $length proof run data modify block 0 64 0 "front_text.messages" set value 1"""u8.ToArray());
                _ = builder.Build(snapshot, [patch]);
                return;
            }
            Put(world, Reader, Encoding.UTF8.GetBytes(getter + "front_text.\"messages\"[1]"));
            Put(world, relay, "function proof:read"u8.ToArray());
            Put(world, caller, Encoding.UTF8.GetBytes(call));
            Reject<EditRejectedException>(() => builder.Build(snapshot, [patch]));
            Put(world, caller, Encoding.UTF8.GetBytes(call.Replace("store result", "store success", StringComparison.Ordinal)));
            _ = builder.Build(snapshot, [patch]);
            Put(world, Reader, Encoding.UTF8.GetBytes(Set + "'{\"text\":\"A different prompt\"}'"));
            Reject<EditRejectedException>(() => builder.Build(snapshot, [patch]));

            Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3681, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(world, Reader, Encoding.UTF8.GetBytes(getter + "front_text.messages[1]"));
            Put(world, caller, Encoding.UTF8.GetBytes(call));
            var explicitReturn = query.QueryAsync(new(new(new SnapshotScope([], [], [], [Writer])))).GetAwaiter().GetResult().Snapshot!;
            _ = builder.Build(explicitReturn, [patch]);
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    internal static void PreStorageResultCannotRebind()
    {
        var world = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string terrain = "region/r.0.0.mca";
        try
        {
            var level = new NbtCompound([new("DataVersion", new NbtInt(1976)), new("version", new NbtInt(19133)),
                new("RandomSeed", new NbtLong(0)), new("SpawnX", new NbtInt(0)), new("SpawnZ", new NbtInt(0))]);
            Put(world, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", level)])), NbtCompression.GZip));
            Put(world, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":4,"description":""}}"""u8.ToArray());
            var sign = new NbtCompound([new("id", new NbtString("minecraft:sign")), new("Text1", new NbtString("{\"text\":\"Volcanic\"}"))]);
            var payload = new NbtCompound([new("xPos", new NbtInt(0)), new("zPos", new NbtInt(0)), new("TileEntities", new NbtList(NbtType.Compound, [sign]))]);
            var chunk = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(1976)), new("Level", payload)]));
            Put(world, terrain, RegionFile.Read(new byte[8192]).WithChunk(0, chunk, NbtCompression.Zlib, 1));
            var inventory = new WorldInventory(world, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var snapshot = query.QueryAsync(new(new(new SnapshotScope([], [], [], [terrain])))).GetAwaiter().GetResult().Snapshot!;
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Text == "Volcanic").ToSlot("en_us", "zh_tw");
            var patch = new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山")));
            var builder = new WorldPlanBuilder(inventory);
            _ = builder.Build(snapshot, [patch]);
            Put(world, Reader, "execute store result score #value proof run data get block 0 64 0 Text1"u8.ToArray());
            Reject<EditRejectedException>(() => builder.Build(snapshot, [patch]));
            Put(world, Reader, "execute store success score #value proof run data get block 0 64 0 Text1"u8.ToArray());
            _ = builder.Build(snapshot, [patch]);
        }
        finally { Directory.Delete(world, true); Directory.Delete(cache, true); }
    }

    internal static void NonSignPathDoesNotAllocateSegments()
    {
        var address = new SourceAddress(new(World, "proof.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var path = string.Join('.', Enumerable.Repeat("key", 32_768));
        var plain = new CommandCandidate(address, "data get block 0 64 0 " + path);
        var observed = new CommandCandidate(address, "execute store result score $value proof run " + plain.Command);
        _ = Read(plain);
        _ = Read(observed);
        var extra = Read(observed) - Read(plain);
        Require(extra < 65_536, $"an unrelated NBT path should not allocate a second set of segments: {extra}");

        long Read(CommandCandidate candidate)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var complete = CommandTextExtractor.Extract(candidate, profile, runtimeDataVersion: 3465).Complete;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(complete, "the unrelated path is valid in both forms");
            return allocated;
        }
    }

    private static void Initialize(string world)
    {
        Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
        Put(world, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
    }

    internal static void StalePolicyCanOnlyRollback()
    {
        var world = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            Initialize(world);
            var original = Encoding.UTF8.GetBytes(Set + "'{\"text\":\"Volcanic \"}'");
            Put(world, Writer, original);
            var inventory = new WorldInventory(world, World);
            var storage = new ArtifactStorage(world, World);
            var snapshot = WorldQueryTests.Query(world, Path.Combine(state, "cache"));
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences.Single().ToSlot("en_us", "zh_tw");
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("sign", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "火山 ")))]);
            var prior = new PatchPlan(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, "world-prepare-v1+text-result-proof-v1", plan.Operations,
                plan.Preconditions, plan.Outputs, plan.Dependencies, plan.Recovery);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
            var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
            var backup = new WorldBackupService(inventory, objects).Create(prior.Preconditions);
            PreparedManifest manifest;
            byte[] image;
            using (var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true))
            {
                manifest = new PreparedManifest(prior, prepared.Manifest.Artifacts);
                image = File.ReadAllBytes(manifest.Artifacts.Single().CandidatePath!);
            }
            objects.SaveBlob(Hash256.Compute(image), image);
            var header = new ApplyJournalHeader(prior, manifest, backup.ManifestHash, storage.AccessBinding);
            var headerHash = Store(Encoding.UTF8.GetBytes(ApplyJournalHeaderJson.Write(header)));
            var record = Encoding.UTF8.GetBytes(ApplyJournalJson.Write(new(JournalBinding.From(manifest), headerHash, 0, null,
                ApplyDirection.Forward, ApplyJournalPhase.Prepared, 0, null)));
            _ = Store(record);
            journals.Write(prior.PlanHash, record, null);

            var engine = new WorldApplyEngine(inventory, storage, objects, journals);
            Reject<ReferencePolicyException>(() => engine.Recover(prior.PlanHash, true));
            Require(File.ReadAllBytes(Path.Combine(world, Writer)).SequenceEqual(original) && journals.Read(prior.PlanHash)!.SequenceEqual(record),
                "a stale forward journal cannot install its postimage or advance its record");
            Put(world, Writer, image);
            Require(engine.Recover(prior.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 1
                && File.ReadAllBytes(Path.Combine(world, Writer)).SequenceEqual(original), "the stale journal still restores its exact original image");

            Hash256 Store(byte[] bytes)
            {
                var hash = Hash256.Compute(bytes);
                objects.SaveBlob(hash, bytes);
                return hash;
            }
        }
        finally { Directory.Delete(world, true); Directory.Delete(state, true); }
    }
}
