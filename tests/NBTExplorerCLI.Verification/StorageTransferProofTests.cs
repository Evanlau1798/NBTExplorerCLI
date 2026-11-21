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

internal static class StorageTransferProofTests
{
    internal static readonly VerificationCase[] Cases =
    [
        new("command-native-storage-transfer-syntax", "discovery", _ => StorageOperations()),
        new("prepare-storage-preserves-imported-sign-text", "preparation", _ => SignImport())
    ];

    private static void StorageOperations()
    {
        var address = new SourceAddress(new(World, "transfer.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        const string copy = "data modify storage demo:out payload set from storage demo:in value";
        const string slice = "data modify storage demo:out payload append string storage demo:in value -2 -1";
        const string observe = "execute store success score #copied proof run ";
        var accesses = new List<CommandStorageAccess>();
        Require(!Parse(copy, 2204).Complete && Parse(copy, 2205).Complete
            && !Parse(slice, 3319).Complete && Parse(slice, 3320).Complete, "native transfer syntax is introduced at its actual version boundary");
        Require(Parse(copy, 3465, accesses.Add).Complete
            && accesses is [{ Use: CommandStorageUse.ListCopy, Storage: "demo:out", Path: "payload", CopySource: ("demo:in", "value") }],
            "an ordinary copy records an exact dependency without projecting a new display slot");
        foreach (var command in new[] { observe + copy, slice })
        {
            accesses.Clear();
            var result = Parse(command, 3465, accesses.Add);
            Require(result.Complete && result.Text.Occurrences.IsEmpty
                && accesses.Select(access => (access.Storage, access.Path, access.Use)).SequenceEqual(new[]
                {
                    ("demo:out", "payload", CommandStorageUse.Other), ("demo:in", "value", CommandStorageUse.Other),
                }), "an observed copy or string transformation still protects both raw operands");
        }
        Require(Parse("data modify storage demo:out payload set string storage demo:in", 3465).Complete
            && Parse("data modify storage demo:out payload insert 0 string storage demo:in value 1", 3465).Complete,
            "whole-source and optional index forms retain their native syntax");
        foreach (var command in new[] { slice + " 0", slice.Replace("-2", "0.5", StringComparison.Ordinal), copy + "[{Display:\"literal\"}]",
            copy.Replace("storage demo:out payload", "entity @s CustomName", StringComparison.Ordinal),
            slice.Replace("storage demo:in value", "block 0 64 0 front_text.messages[0]", StringComparison.Ordinal) })
            Require(!Parse(command, 3465).Complete, "unproven operands and targets stay outside the storage-only transfer rules");

        CommandExtractionResult Parse(string command, int version, Action<CommandStorageAccess>? observeStorage = null)
            => CommandTextExtractor.Extract(new(address, command), MinecraftProfiles.ForDataVersion(version)!,
                storageAccess: observeStorage, runtimeDataVersion: version);
    }

    private static void SignImport()
    {
        const string folder = "datapacks/p/data/proof/functions/";
        const string writer = folder + "write.mcfunction";
        const string transfer = folder + "transfer.mcfunction";
        const string initial = """data modify storage demo:display lines append value '{"text":"Bienvenue"}'""";
        const string import = "data modify storage demo:display lines append from block 0 64 0 front_text.messages[1]";
        var world = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(world, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":""}}"""u8.ToArray());
            Put(world, writer, Encoding.UTF8.GetBytes(initial));
            Put(world, transfer, Encoding.UTF8.GetBytes(import));
            Put(world, folder + "read.mcfunction", """tellraw @a {"nbt":"lines[]","storage":"demo:display","interpret":true}"""u8.ToArray());
            var cache = Path.Combine(state, "cache");
            var snapshot = WorldQueryTests.Query(world, cache);
            Require(snapshot.Manifest.CoverageComplete, "an unobserved sign-string import participates in a text list");
            var rows = snapshot.ReadSources().ToArray();
            Require(rows.Single(row => row.Source.Location.Artifact.RelativePath == transfer).Text!.Occurrences.IsEmpty,
                "the imported value does not become a new editable slot");
            var text = rows.SelectMany(row => row.Text?.Occurrences ?? []).Single(row => row.Text == "Bienvenue");
            var slot = text.ToSlot("fr_fr", "de_de");
            var edit = new LiteralPatch("translate", new(slot, new(slot.Id, slot.SourceTextHash, "de_de", "Willkommen")));
            var inventory = new WorldInventory(world, World);
            var storage = new ArtifactStorage(world, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [edit]);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(world, state, storage);
            Require(engine.Apply(prepared, true).Success && File.ReadAllText(Path.Combine(world, writer)).Contains("Willkommen", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(world, transfer)) == import, "the selected prose changes while the import stays byte-identical");
            Require(engine.Recover(plan.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 1
                && File.ReadAllText(Path.Combine(world, writer)) == initial, "the original writer can be restored exactly");

            Put(world, folder + "observed.mcfunction",
                "execute store result score #size proof run function proof:transfer"u8.ToArray());
            Require(!WorldQueryTests.Query(world, cache).Manifest.CoverageComplete, "an old caller that observes the transfer result revokes the slot");
            Put(world, folder + "observed.mcfunction",
                "execute store result score #size proof run data get storage demo:display lines[0]"u8.ToArray());
            Require(!WorldQueryTests.Query(world, cache).Manifest.CoverageComplete, "an exact raw storage reader revokes the slot");
        }
        finally { Directory.Delete(world, true); Directory.Delete(state, true); }
    }
}
