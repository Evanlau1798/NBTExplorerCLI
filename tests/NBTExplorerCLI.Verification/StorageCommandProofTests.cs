using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.CliScanTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageCommandProofTests
{
    private const string Folder = "datapacks/p/data/demo/functions/";
    private const string NamespacedFolder = "datapacks/p/data/demo/function/";
    private const string Producer = Folder + "producer.mcfunction";
    private const string Consumer = Folder + "consumer.mcfunction";
    private const string Writer = """data modify storage demo:messages lines append value '{"text":"Welcome"}'""";
    private const string Reader = """tellraw @a {"storage":"demo:messages","nbt":"lines[]","interpret":true}""";
    private static readonly string LiteralReader = Reader.Replace("true", "false", StringComparison.Ordinal);

    internal static void CrossFileDisplayConsumers()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Create(root);
            Put(root, Consumer, Encoding.UTF8.GetBytes(LiteralReader));
            Put(root, Folder + "opaque.mcfunction", Encoding.UTF8.GetBytes(Writer.Replace("demo:messages", "demo:opaque", StringComparison.Ordinal)));
            TextSourceExtraction WriteSource(string name) => WorldQueryTests.Query(root, cache).ReadSources()
                .Single(row => row.Source.Location.Artifact.RelativePath == Folder + name + ".mcfunction");
            Require(WriteSource("producer").Text!.Occurrences.IsEmpty, "a literal-mode consumer does not authorize editing a JSON component inside storage");
            Put(root, Consumer, Encoding.UTF8.GetBytes(Reader + "\n" + LiteralReader));
            Require(WriteSource("producer").Text!.Occurrences.IsEmpty, "a raw consumer of the same storage path leaves the interpreted producer ambiguous");
            Put(root, Consumer, Encoding.UTF8.GetBytes(Reader + "\ndata get storage demo:messages lines[]"));
            Require(WriteSource("producer").Text!.Occurrences.IsEmpty, "ordinary data reads of the same storage prevent inferring an exclusive display role");
            Put(root, Consumer, Encoding.UTF8.GetBytes(Reader));
            var display = WriteSource("producer");
            Require(display.Outcome == SourceOutcome.Success && display.Text!.Occurrences.Single().Text == "Welcome",
                "a proven interpreted storage consumer identifies component text at its cross-file producer");
            Require(WriteSource("opaque").Text!.Occurrences.IsEmpty, "JSON syntax alone does not authorize edits to unrelated storage");
            PutSeed(root, S("not a list"));
            Require(WriteSource("producer").Text!.Occurrences.IsEmpty, "an incompatible saved value invalidates the cached producer despite unchanged commands");
            PutSeed(root, new NbtList(NbtType.String, []));
            Put(root, Consumer, Encoding.UTF8.GetBytes(LiteralReader));
            Require(WriteSource("producer").Text!.Occurrences.IsEmpty, "a changed consumer invalidates the cached producer role even when its own bytes have not changed");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void ItemMetadataIsNotDisplayText()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Create(root, seed: false);
            Put(root, Producer, """data modify storage demo:messages stack.tag.Enchantments append value {id:"minecraft:sharpness",lvl:1s}"""u8.ToArray());
            Put(root, Consumer, """tellraw @a {"storage":"demo:messages","nbt":"stack.tag.Enchantments[]","interpret":true}"""u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var rows = snapshot.ReadSources().ToDictionary(row => row.Source.Location.Artifact.RelativePath);
            Require(rows[Producer].Outcome == SourceOutcome.Irrelevant && rows[Producer].Text!.Occurrences.IsEmpty,
                "a conflicting interpretation must not misparse an item metadata writer as display text");
            Require(rows[Consumer].Outcome == SourceOutcome.Unsupported && !snapshot.Manifest.CoverageComplete,
                "an interpreted reader of nontext metadata retains the unresolved diagnostic");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void AliasedSeedBeforeScan(bool namespaced = false)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var producerPath = Producer;
            if (namespaced)
            {
                producerPath = CreateNamespaced(root, "demo");
                Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete, "the namespaced empty-storage control proves the common command shape");
            }
            else Create(root, seed: false);
            PutAliasedSeed(root, namespaced);
            var snapshot = WorldQueryTests.Query(root, cache);
            var producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == producerPath);
            Require(!snapshot.Manifest.CoverageComplete && producer.Text!.Occurrences.IsEmpty,
                "a Windows alias for the saved seed cannot authorize a producer as if storage were absent");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void NamespacedLogicalAliases(bool mixedReaders)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            if (mixedReaders)
            {
                CreateNamespaced(root, "demo.site");
                Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete, "a dot within a namespace does not create a Windows path alias");
                Put(root, NamespacedFolder + "opaque.mcfunction", "data get storage demo.site.:messages lines[]"u8.ToArray());
            }
            else
            {
                CreateNamespaced(root, "demo.");
                PutSeed(root, L(new NbtInt(73)), "data/demo/command_storage.dat", 5130);
            }
            var snapshot = WorldQueryTests.Query(root, cache);
            var producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == NamespacedFolder + "producer.mcfunction");
            Require(!snapshot.Manifest.CoverageComplete && producer.Text!.Occurrences.IsEmpty,
                "a trailing-dot storage namespace cannot bypass a saved seed or an ordinary reader on Windows");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void AliasedSeedAfterPreparation(bool recover, bool namespaced = false)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var producer = Producer;
            if (namespaced) producer = CreateNamespaced(root, "demo");
            else Create(root, seed: false);
            var original = File.ReadAllText(Path.Combine(root, producer));
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == producer)
                .Text!.Occurrences.Single().ToSlot("en_us", "zh_tw");
            var inventory = new WorldInventory(root, World);
            var operation = new LiteralPatch("storage-text", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "歡迎")));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            var storage = recover ? new ArtifactStorage(root, World, new WorldApplyTests.FailReplace(1, after: false)) : new ArtifactStorage(root, World);
            var engine = WorldApplyTests.Engine(root, state, storage);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            if (recover)
                Require(engine.Apply(prepared, true).Counts[OperationOutcome.RecoveryRequired] == 1, "the interruption leaves a forward storage write pending");
            if (namespaced) Put(root, NamespacedFolder + "opaque.mcfunction", "data get storage demo.:messages lines[]"u8.ToArray());
            else PutAliasedSeed(root);
            if (recover)
                Require(!WorldApplyTests.Engine(root, state).Recover(plan.PlanHash, true).Success, "a late Windows storage alias prevents forward recovery");
            else
                Reject<ArtifactConflictException>(() => engine.Apply(prepared, true));
            Require(File.ReadAllText(Path.Combine(root, producer)) == original, "rejection leaves the original producer bytes intact");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void WorldEvidenceBudget()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Create(root);
            var limits = new DecodeLimits(MaxDecodedBytes: 4096);
            WorldTextExtraction Read() => new(new WorldInventory(root, World), new(SnapshotScope.All, limits));
            for (var index = 0; index < 24; index++)
                Put(root, Folder + $"producer{index}.mcfunction", Encoding.UTF8.GetBytes(Writer.Replace(" lines ", " lines_other ", StringComparison.Ordinal)));
            Require(!Read().ReadSources().Any(row => row.DiagnosticCode == "LimitExceeded"), "repeated matched paths across the same files stay within the evidence budget");
            for (var index = 0; index < 24; index++)
                Put(root, Folder + $"producer{index}.mcfunction", Encoding.UTF8.GetBytes(Writer.Replace(" lines ", $" lines_{index:D2} ", StringComparison.Ordinal)));
            Require(Read().ReadSources().Any(row => row.DiagnosticCode == "LimitExceeded"),
                "distinct matched producer paths across files count against the storage evidence budget");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void PreparationRevalidatesStorageUses()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            Create(root);
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "complete storage producer and consumer evidence admits this world to a translation session");
            var occurrence = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer)
                .Text!.Occurrences.Single();
            var slot = occurrence.ToSlot("en_us", "zh_tw");
            var operation = new LiteralPatch("storage-text", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "歡迎")));
            var inventory = new WorldInventory(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            var storage = new ArtifactStorage(root, World);
            var preparation = new WorldPlanPreparation(inventory, storage);
            var engine = WorldApplyTests.Engine(root, state, storage);
            using (var prepared = preparation.Prepare(snapshot, plan, true))
            {
                var candidate = File.ReadAllText(prepared.Manifest.Artifacts.Single().CandidatePath!);
                const string prefix = "data modify storage demo:messages lines append value ";
                Require(candidate.StartsWith(prefix, StringComparison.Ordinal)
                    && File.ReadAllText(Path.Combine(root, Producer)) == Writer, "preparation preserves command structure and the original file");
                var component = Snbt.ParseDocument(Encoding.UTF8.GetBytes(candidate[prefix.Length..])).Root.Value;
                if (component is not LogicalMinecraftLeaf { Value: NbtString stored })
                    throw new InvalidOperationException("The candidate lost the stored JSON string layer");
                using var json = JsonDocument.Parse(stored.Value);
                Require(json.RootElement.GetProperty("text").GetString() == "歡迎", "the visible storage component is translated in the candidate");
            }

            var originals = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Hash256.Compute(File.ReadAllBytes(path)));
            const string lateReader = Folder + "late-reader.mcfunction";
            using (var prepared = preparation.Prepare(snapshot, plan, true))
            {
                Put(root, lateReader, Encoding.UTF8.GetBytes(LiteralReader));
                Reject<ArtifactConflictException>(() => engine.Apply(prepared, true));
                Require(originals.All(item => Hash256.Compute(File.ReadAllBytes(item.Key)) == item.Value), "a new consumer invalidates a previously staged write");
                File.Delete(Path.Combine(root, lateReader));
            }
            using (var prepared = preparation.Prepare(snapshot, plan, true))
            {
                var applied = engine.Apply(prepared, true);
                Require(applied.Success && applied.Counts[OperationOutcome.Applied] == 1, "the proven producer can commit with durable readback");
                Put(root, lateReader, Encoding.UTF8.GetBytes(LiteralReader));
                var reversed = engine.Recover(plan.PlanHash, true, rollback: true);
                Require(reversed.Counts[OperationOutcome.RolledBack] == 1 && originals.All(item => Hash256.Compute(File.ReadAllBytes(item.Key)) == item.Value),
                    "rollback restores all original hashes while preserving a newly added reader");
                Require(File.ReadAllText(Path.Combine(root, lateReader)) == LiteralReader, "rollback cannot modify an unplanned source");
                File.Delete(Path.Combine(root, lateReader));
            }
            Put(root, Consumer, Encoding.UTF8.GetBytes(LiteralReader));
            Reject<ArtifactConflictException>(() => preparation.Prepare(snapshot, plan, true).Dispose());

            Put(root, Consumer, Encoding.UTF8.GetBytes(Reader));
            Put(root, Folder + "indirect-writer.mcfunction", """data modify storage demo:indirect lines append value '{"storage":"demo:messages","nbt":"lines[]","interpret":false}'"""u8.ToArray());
            Put(root, Folder + "indirect-reader.mcfunction", Encoding.UTF8.GetBytes(Reader.Replace("demo:messages", "demo:indirect", StringComparison.Ordinal)));
            var indirect = WorldQueryTests.Query(root, cache);
            Reject<EditRejectedException>(() => new WorldPlanBuilder(inventory).Build(indirect, [operation]));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    internal static void CliArchiveSession()
    {
        var root = ScratchDirectory.CreateChild(); var work = ScratchDirectory.CreateChild();
        const string path = "datapacks/storage.zip";
        const string entry = "data/demo/functions/producer.mcfunction";
        try
        {
            var level = WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions);
            Put(root, "level.dat", CompressedNbt.WriteDocument(level, NbtCompression.GZip));
            PutSeed(root, new NbtList(NbtType.String, []));
            var original = Zip(("pack.mcmeta", """{"pack":{"pack_format":15,"description":"Storage test"}}"""),
                (entry, Writer), ("data/demo/functions/consumer.mcfunction", Reader));
            Put(root, path, original);
            using var scan = Success(Run(work, "scan", root, "--no-cache", "--summary"));
            Require(scan.RootElement.GetProperty("data").GetProperty("coverageComplete").GetBoolean(), "CLI accepts a complete archived storage flow");
            var (id, revision) = CliKeyPlanTests.Session(root, work, "^Welcome$");
            var planFile = Path.Combine(work, "storage-plan.json"); var state = Path.Combine(work, "state");
            var operation = Guid.NewGuid().ToString();
            using var planned = Success(Run(work, "translate", "plan", root, "--direct", "--session-id", id.ToString(), "--revision", revision.ToString(),
                "--state-root", state, "--operation-id", operation, "--output", planFile));
            var plan = PatchPlanJson.Read(File.ReadAllText(planFile));
            using var applied = Success(Run(work, "apply", root, "--plan", planFile, "--world-offline", "--state-root", state));
            using (var archive = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(root, path))))
            using (var reader = new StreamReader(archive.GetEntry(entry)!.Open(), Encoding.UTF8))
                Require(reader.ReadToEnd().Contains("譯:Welcome", StringComparison.Ordinal), "the translated text lands inside the archived producer");
            using var sealedResult = Success(Run(work, "translate", "seal", root, "--session-id", id.ToString(), "--revision", revision.ToString(),
                "--state-root", state, "--operation-id", operation, "--plan-hash", plan.PlanHash.Hex));
            var reverted = Run(work, "recover", root, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", state);
            Require(reverted.Exit == 8 && File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "CLI rollback restores exact archive bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    internal static void WholeListReplacements()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        const string alternate = Folder + "alternate.mcfunction";
        const string reset = Folder + "reset.mcfunction";
        const string first = """data modify storage demo:messages lines set value ['{"text":"First"}','{"text":"Heading"}']""";
        const string second = """data modify storage demo:messages lines set value ['{"text":"Second"}','{"text":"Ending"}']""";
        try
        {
            Create(root);
            Put(root, Producer, Encoding.UTF8.GetBytes(first));
            Put(root, alternate, Encoding.UTF8.GetBytes(second));
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete, "literal list replacements with an exclusive interpreted reader are fully projected");
            var slots = snapshot.ReadSources().Where(row => row.Source.Location.Artifact.RelativePath is Producer or alternate)
                .SelectMany(row => row.Text!.Occurrences).ToArray();
            Require(slots.Select(slot => slot.Text).Order().SequenceEqual(new[] { "Ending", "First", "Heading", "Second" }),
                "each whole-list writer contributes independent ordered component spans");
            var operations = slots.Select((occurrence, index) =>
            {
                var slot = occurrence.ToSlot("en_us", "zh_tw");
                return new LiteralPatch("list-text-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "譯：" + slot.SourceText)));
            }).ToArray();
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var engine = WorldApplyTests.Engine(root, state, storage);
            Require(engine.Apply(prepared, true).Success, "both independently bound list writers can be committed");
            static string[] Values(string command)
            {
                var value = command[(command.IndexOf(" set value ", StringComparison.Ordinal) + 11)..];
                var list = (LogicalMinecraftList)Snbt.ParseDocument(Encoding.UTF8.GetBytes(value)).Root.Value;
                return list.Items.Select(item =>
                {
                    using var component = JsonDocument.Parse(((NbtString)((LogicalMinecraftLeaf)item).Value).Value);
                    return component.RootElement.GetProperty("text").GetString()!;
                }).ToArray();
            }
            Require(Values(File.ReadAllText(Path.Combine(root, Producer))).SequenceEqual(new[] { "譯：First", "譯：Heading" })
                && Values(File.ReadAllText(Path.Combine(root, alternate))).SequenceEqual(new[] { "譯：Second", "譯：Ending" }), "candidate readback preserves each branch and its list order");
            Require(engine.Recover(plan.PlanHash, true, rollback: true).Counts[OperationOutcome.RolledBack] == 4
                && File.ReadAllText(Path.Combine(root, Producer)) == first && File.ReadAllText(Path.Combine(root, alternate)) == second,
                "rollback restores both original command byte streams");
            PutSeed(root, new NbtList(NbtType.Int, []));
            Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete,
                "a replacement is independent of the declared element type of an empty saved list");
            Put(root, alternate, Encoding.UTF8.GetBytes(Writer));
            Require(!WorldQueryTests.Query(root, cache).Manifest.CoverageComplete,
                "an element append still needs a compatible saved list type");
            Put(root, alternate, Encoding.UTF8.GetBytes(second));
            File.Delete(Path.Combine(root, "data/command_storage_demo.dat"));
            Require(WorldQueryTests.Query(root, cache).Manifest.CoverageComplete,
                "literal list replacements also work without a saved seed");
            Put(root, reset, "data modify storage demo:messages lines set value [ ]"u8.ToArray());
            var withReset = WorldQueryTests.Query(root, cache);
            Require(withReset.Manifest.CoverageComplete && withReset.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer).Text!.Occurrences.Length == 2,
                "an empty list reset does not invalidate an exclusive interpreted use");
            Put(root, Consumer, Encoding.UTF8.GetBytes(Reader + "\ndata get storage demo:messages lines[]"));
            var mixed = WorldQueryTests.Query(root, cache);
            Require(!mixed.Manifest.CoverageComplete && mixed.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer).Text!.Occurrences.IsEmpty,
                "a raw reader invalidates the cached whole-list interpretation");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(state, true); }
    }

    private static void Create(string root, bool seed = true)
    {
        var level = WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions);
        Put(root, "level.dat", CompressedNbt.WriteDocument(level, NbtCompression.GZip));
        if (seed) PutSeed(root, new NbtList(NbtType.String, []));
        Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Test"}}"""u8.ToArray());
        Put(root, Producer, Encoding.UTF8.GetBytes(Writer));
        Put(root, Consumer, Encoding.UTF8.GetBytes(Reader));
    }

    private static string CreateNamespaced(string root, string space)
    {
        const int version = 5130;
        const string producer = NamespacedFolder + "producer.mcfunction";
        Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(version), NbtCompression.GZip));
        Put(root, "data/minecraft/world_gen_settings.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Separate(version, WorldMetadataTests.Dimensions), NbtCompression.GZip));
        Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"min_format":128,"max_format":128,"description":"Test"}}"""u8.ToArray());
        Put(root, producer, Encoding.UTF8.GetBytes($$"""data modify storage {{space}}:messages lines append value {text:"Welcome"}"""));
        Put(root, NamespacedFolder + "consumer.mcfunction", Encoding.UTF8.GetBytes($$"""tellraw @a {storage:"{{space}}:messages",nbt:"lines[]",interpret:true}"""));
        return producer;
    }

    private static void PutAliasedSeed(string root, bool namespaced = false)
    {
        var canonical = namespaced ? "data/demo/command_storage.dat" : "data/command_storage_demo.dat";
        Require(!File.Exists(Path.Combine(root, canonical)), "the native seed path starts empty");
        var alias = namespaced ? "data/DEMO/COMMAND_STORAGE.DAT" : "data/COMMAND_STORAGE_DEMO.DAT";
        PutSeed(root, L(namespaced ? C(E("text", S("Already saved"))) : S("""{"text":"Already saved"}""")), alias, namespaced ? 5130 : 3465);
        Require(File.Exists(Path.Combine(root, canonical)), "the fixture requires the native Windows alias to resolve");
    }

    private static void PutSeed(string root, NbtValue value, string path = "data/command_storage_demo.dat", int version = 3465)
    {
        var seed = C(E("DataVersion", new NbtInt(version)), E("data", C(E("contents", C(E("messages", C(E("lines", value))))))));
        Put(root, path, CompressedNbt.WriteDocument(new("", seed), NbtCompression.GZip));
    }
}
