using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageBlankProbeTests
{
    private const string Folder = "datapacks/p/data/proof/functions/";
    private const string Writer = Folder + "write.mcfunction";
    private const string Probe = Folder + "probe.mcfunction";
    private const string Initial = """data modify storage proof:labels lines set value ['{"text":"Open this gate"}','{"text":"\\u0020"}']""";
    private const string Reader = """tellraw @a {"storage":"proof:labels","nbt":"lines[]","interpret":true}""";
    private const string Comparison = """execute store success score #probe results run data modify storage proof:labels lines[0] set value '{"text":" "}'""";
    private const string Reset = """data modify storage proof:labels scratch set value '{"text":" "}'""";
    private const string Copy = "execute store success score #probe results run data modify storage proof:labels scratch set from storage proof:labels lines[0]";

    internal static void BlankEditsAreRejected()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Create(root, Reader);
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "the display-only control has complete coverage");
            var builder = new WorldPlanBuilder(new WorldInventory(root, World));
            var slots = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences;
            var prose = slots.Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
            var blank = slots.Single(item => item.Text == " ").ToSlot("en_us", "zh_tw");
            Require(prose.Validate(new(prose.Id, prose.SourceTextHash, "zh_tw", " ")) != SlotValidation.Valid,
                "session validation rejects creation of a blank sentinel");
            foreach (var (slot, text) in new[] { (prose, ""), (prose, " "), (blank, ""), (blank, "  "), (blank, "Visible") })
                Reject<ArgumentException>(() => builder.Build(snapshot, [Patch(slot, text)]));
            _ = builder.Build(snapshot, [Patch(prose, "打開這扇門")]);

            Put(root, Writer, Encoding.UTF8.GetBytes(Reset));
            Put(root, Probe, """tellraw @a {"storage":"proof:labels","nbt":"scratch","interpret":true}"""u8.ToArray());
            var scalar = WorldQueryTests.Query(root, cache);
            Require(scalar.Manifest.CoverageComplete, "a proven display-only blank scalar retains coverage without a comparison");
            var scalarBlank = scalar.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == " ").ToSlot("en_us", "zh_tw");
            Require(scalarBlank.Validate(new(scalarBlank.Id, scalarBlank.SourceTextHash, "zh_tw", "Visible")) != SlotValidation.Valid,
                "the independent display proof still protects the original blank scalar");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void ObservedIndexedBlankWriteAllowsTranslationAndRollback()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            Create(root, Comparison + "\r\n" + Reader);
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "a fixed indexed blank comparison preserves a provable interpreted list");
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [Patch(slot, "打開這扇門")]);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state, storage);
            Require(engine.Apply(prepared, true).Success, "the ordinary translation can be committed");
            var writer = Path.Combine(root, Writer);
            Require(File.ReadAllText(writer).Contains("打開這扇門", StringComparison.Ordinal)
                && File.ReadAllText(writer).Contains("\\u0020", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(root, Probe)) == Comparison + "\r\n" + Reader,
                "the command, comparison and original escaped neighbour are preserved");
            Require(engine.Recover(plan.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 1
                && File.ReadAllBytes(writer).SequenceEqual(Encoding.UTF8.GetBytes(Initial)), "rollback restores the original writer bytes");

            Put(root, Probe, Encoding.UTF8.GetBytes(Comparison.Replace("#probe", "$sample", StringComparison.Ordinal) + "\n" + Reader));
            Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete, "an early-profile fixed dollar holder is supported");

            foreach (var unsupported in new[]
            {
                Comparison.Replace("[0]", "[]", StringComparison.Ordinal),
                Comparison.Replace("\" \"", "\"Open this gate\"", StringComparison.Ordinal),
                Comparison.Replace("\"text\":\" \"", "\"text\":\" \",\"font\":\"proof:meter\"", StringComparison.Ordinal),
                "execute if score #flag results matches 1 run " + Comparison[(Comparison.IndexOf("data ", StringComparison.Ordinal))..],
                "execute as @a run " + Comparison[(Comparison.IndexOf("data ", StringComparison.Ordinal))..],
                Comparison.Replace("#probe", "*", StringComparison.Ordinal)
            })
            {
                Put(root, Probe, Encoding.UTF8.GetBytes(unsupported + "\n" + Reader));
                Require(!WorldQueryTests.Query(root, cache).Manifest.CoverageComplete,
                    "wildcard, nonblank and styled comparison writes stay unresolved");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void ResetScratchComparisonRequiresExclusiveUse()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var control = Reset + "\r\n" + Copy + "\r\n" + Reader;
            Create(root, control);
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "an adjacent unconditional reset and exclusive scratch comparison preserve the display proof");
            Put(root, Probe, Encoding.UTF8.GetBytes(control.Replace("#probe", "$sample", StringComparison.Ordinal)));
            Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete, "the reset pair also accepts a fixed dollar holder");
            Put(root, Probe, Encoding.UTF8.GetBytes(control));
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [Patch(slot, "打開這扇門")]);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            Put(root, Folder + "late.mcfunction", "data get storage proof:labels scratch"u8.ToArray());
            var engine = WorldApplyTests.Engine(root, state, storage);
            Reject<ArtifactConflictException>(() => engine.Apply(prepared, true));
            Require(File.ReadAllText(Path.Combine(root, Writer)) == Initial, "a late raw scratch reader prevents all staged mutations");
            File.Delete(Path.Combine(root, Folder + "late.mcfunction"));

            foreach (var unsafeScript in new[]
            {
                Copy + "\n" + Reader,
                Reset + "\n" + Reader,
                "execute if score #flag results matches 1 run " + control,
                Reset + "\ndata modify storage proof:labels scratch set value \"Other\"\n" + Copy + "\n" + Reader,
                Reset + "\n" + Copy.Replace("execute store", "execute as @a store", StringComparison.Ordinal) + "\n" + Reader,
                Reset + "\n" + Copy.Replace("[0]", "[]", StringComparison.Ordinal) + "\n" + Reader,
                Reset + "\n" + Copy.Replace("#probe", "@a", StringComparison.Ordinal) + "\n" + Reader,
                control.Replace(" scratch ", " nested.scratch ", StringComparison.Ordinal),
                control + "\ndata get storage proof:labels scratch",
                control + "\ndata modify storage proof:labels saved set from storage proof:labels scratch"
            })
            {
                Put(root, Probe, Encoding.UTF8.GetBytes(unsafeScript));
                var unsafeSnapshot = WorldQueryTests.Query(root, cache);
                Require(!unsafeSnapshot.Manifest.CoverageComplete, "ambiguous reset, fan-out or a published scratch value cannot authorize writes");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void BlankComparisonRequiresProvenSource()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var rawProducer = """data modify storage proof:raw lines append from block 0 64 0 front_text.messages[0]""";
            foreach (var comparison in new[] { Comparison, Reset + "\n" + Copy })
            {
                var rawComparison = comparison.Replace("proof:labels lines[0]", "proof:raw lines[0]", StringComparison.Ordinal);
                Create(root, rawComparison + "\n" + Reader);
                Put(root, Folder + "raw.mcfunction", Encoding.UTF8.GetBytes(rawProducer));
                var snapshot = WorldQueryTests.Query(root, cache);
                var probe = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Probe);
                Require(probe.Outcome == SourceOutcome.Unsupported && probe.Text is { Diagnostics.IsEmpty: false },
                    "comparison remains unresolved when a raw sign supplies its list");
                Require(!snapshot.Manifest.CoverageComplete, "an unrelated proven list cannot authorize the comparison");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void ScopedSignCannotChangeComparedBlankness()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            const string sign = """data modify block 0 64 0 front_text.messages[0] set value '{"text":"Open this gate"}'""";
            const string import = "data modify storage proof:labels lines append from block 0 64 0 front_text.messages[0]";
            var inventory = new WorldInventory(root, World);
            var storage = new ArtifactStorage(root, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var builder = new WorldPlanBuilder(inventory);
            var scope = new SnapshotScope([], [], [], [Writer]);
            foreach (var sameFile in new[] { true, false })
            {
                Create(root, Comparison);
                Put(root, Writer, Encoding.UTF8.GetBytes(sameFile ? sign + "\n" + import : sign));
                Put(root, Folder + "import.mcfunction", Encoding.UTF8.GetBytes(sameFile ? "" : import));
                var snapshot = query.QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
                Require(snapshot.Manifest.CoverageComplete, "the selected sign is readable for drafting");
                var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                    .Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
                Reject<EditRejectedException>(() => builder.Build(snapshot, [Patch(slot, " ")]));

                Put(root, Probe, """tellraw @a {"text":"Ready"}"""u8.ToArray());
                var plan = builder.Build(snapshot, [Patch(slot, " ")]);
                using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
                var late = Folder + "late.mcfunction";
                Put(root, late, Encoding.UTF8.GetBytes(Comparison));
                Reject<ArtifactConflictException>(() => WorldApplyTests.Engine(root, state, storage).Apply(prepared, true));
                Require(File.ReadAllText(Path.Combine(root, Writer)).StartsWith(sign, StringComparison.Ordinal),
                    "off-scope comparison changes are rejected before changing the sign");
                File.Delete(Path.Combine(root, late));
            }

            Put(root, Writer, Encoding.UTF8.GetBytes(sign.Replace("Open this gate", " ", StringComparison.Ordinal)));
            Put(root, Probe, Encoding.UTF8.GetBytes(Comparison));
            var whitespace = query.QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
            var originalBlank = whitespace.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == " ").ToSlot("en_us", "zh_tw");
            Reject<EditRejectedException>(() => builder.Build(whitespace, [Patch(originalBlank, "  ")]));

            var styled = sign.Replace("""{"text":"Open this gate"}""", """{"italic":false,"text":"Open this gate"}""", StringComparison.Ordinal);
            Put(root, Writer, Encoding.UTF8.GetBytes(styled));
            foreach (var replacement in new[] { " ", "Gate open" })
            {
                var unresolved = Comparison.Replace("""{"text":" "}""", $$"""{"italic":false,"text":"{{replacement}}"}""", StringComparison.Ordinal);
                Put(root, Probe, Encoding.UTF8.GetBytes(unresolved));
                var selected = query.QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
                var signText = selected.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                    .Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
                Reject<EditRejectedException>(() => builder.Build(selected, [Patch(signText, replacement)]));
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void ScopedSignRejectsObservedCopy(bool native)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string sign = """data modify block 0 64 0 front_text.messages[0] set value '{"text":"Open this gate"}'""";
            const string read = "data modify storage proof:labels scratch set from block 0 64 0 front_text.messages[0]";
            Create(root, "");
            Put(root, Writer, Encoding.UTF8.GetBytes(sign));
            var inventory = new WorldInventory(root, World);
            var query = new WorldQueryService(inventory, new FileSystemScanCacheStore(cache));
            var scope = new SnapshotScope([], [], [], [Writer]);
            var snapshot = query.QueryAsync(new(new(scope))).GetAwaiter().GetResult().Snapshot!;
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Writer).Text!.Occurrences
                .Single(item => item.Text == "Open this gate").ToSlot("en_us", "zh_tw");
            var change = Patch(slot, "打開這扇門");
            var builder = new WorldPlanBuilder(inventory);
            PutComparison(read);
            _ = builder.Build(snapshot, [change]);
            if (native)
            {
                PutComparison(Comparison.Replace("""{"text":" "}""", """{"italic":false,"text":" "}""", StringComparison.Ordinal));
                Reject<EditRejectedException>(() => builder.Build(snapshot, [change]));
            }
            foreach (var observation in new[] { "success", "result" })
            {
                PutComparison($"execute store {observation} score #probe results run {read}");
                Reject<EditRejectedException>(() => builder.Build(snapshot, [change]));
            }
            void PutComparison(string command)
            {
                if (!native) { Put(root, Probe, Encoding.UTF8.GetBytes(command)); return; }
                var block = new NbtCompound([new("id", new NbtString("minecraft:command_block")), new("Command", new NbtString(command))]);
                var chunk = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3465)),
                    new("block_entities", new NbtList(NbtType.Compound, [block]))]));
                Put(root, "region/r.0.0.mca", RegionFile.Read(new byte[8192]).WithChunk(0, chunk, NbtCompression.Zlib, 1));
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    private static LiteralPatch Patch(TextSlot slot, string text)
        => new("text", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", text)));

    private static void Create(string root, string probe)
    {
        Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
        Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Test"}}"""u8.ToArray());
        Put(root, Writer, Encoding.UTF8.GetBytes(Initial));
        Put(root, Probe, Encoding.UTF8.GetBytes(probe));
    }
}
