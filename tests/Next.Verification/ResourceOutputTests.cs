using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class ResourceOutputTests
{
    private static ResourceGeneration Request(ResourceOutputFormat format) => new(World, format,
        format == ResourceOutputFormat.LanguageFiles ? "" : format == ResourceOutputFormat.ZipPack ? "resources.zip" : "Translations",
        [new("zh_tw", [new("map.test", "譯文 \ud800 50%"), new("map.empty", "")])], format == ResourceOutputFormat.LanguageFiles ? null : new(34, 0));
    public static void AllFormatsUsePlansReadbackReplayAndRawRollback()
    {
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
                var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
                var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
                var service = new ResourceOutputService(inventory, storage, objects); var request = Request(format); var plan = service.Build(request);
                Require(plan.Operations.Single() is ResourcePatch && plan.Outputs.Length == request.Outputs.Length && plan.Preconditions.All(input => input.Image.ExpectedAbsent), "preview declares exact physical resource outputs and expected absence");
                Require(!Directory.GetFiles(root, "*", SearchOption.AllDirectories).Any(), "preview does not publish output files");
                var engine = new WorldApplyEngine(inventory, storage, objects, journals);
                var receipt = engine.Apply(service.Prepare(plan, true), true);
                Require(receipt.Success && receipt.Results.Single().Outcome == OperationOutcome.Applied && receipt.Results.Single().Proofs.Length == plan.Outputs.Length, "one operation is applied only after every physical output reads back");
                byte[] language;
                if (format == ResourceOutputFormat.ZipPack)
                {
                    using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(root, "resources.zip")));
                    using var input = zip.GetEntry("assets/minecraft/lang/zh_tw.json")!.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); language = bytes.ToArray();
                }
                else language = File.ReadAllBytes(Path.Combine(root, format == ResourceOutputFormat.LanguageFiles ? "zh_tw.json" : "Translations/assets/minecraft/lang/zh_tw.json"));
                using var parsed = BoundedJson.ReadDecoded(language, DecodeLimits.Default);
                Require(NativeJsonStrings.Read(parsed.RootElement.GetProperty("map.test")) == "譯文 \ud800 50%1$s" && NativeJsonStrings.Read(parsed.RootElement.GetProperty("map.empty")) == "", "literal percent adapter and exact UTF16/empty values survive rendering");
                Require(engine.Recover(plan.PlanHash, true).Success, "durable replay uses the existing engine");
                var fresh = service.Build(request); var unchanged = engine.Apply(service.Prepare(fresh, true), true);
                Require(unchanged.Success && unchanged.Results.Single().Outcome == OperationOutcome.Unchanged, "regenerating identical resources preserves bytes and is unchanged");
                var rollback = engine.Recover(plan.PlanHash, true, rollback: true);
                Require(!rollback.Success && rollback.Results.Single().Outcome == OperationOutcome.RolledBack && request.Outputs.All(output => !File.Exists(Path.Combine(root, output.RelativePath))), "original journal rolls back every output to exact absence");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void GameplayScopesCannotBorrowResourceOutputAuthority()
    {
        foreach (var shape in new[] { "root", "data", "datapacks", "region", "entities", "poi", "players", "playerdata", "dimensions/custom/x", "DIM-1", "data/nested/resourcepacks" })
        {
            var world = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Level(world, false); var path = shape == "root" ? world : Path.Combine(world, shape); Directory.CreateDirectory(path);
                if (shape == "data/nested/resourcepacks") Level(Path.GetDirectoryName(path)!, false);
                var inventory = new WorldInventory(path, World); var storage = new ArtifactStorage(path, World);
                var service = new ResourceOutputService(inventory, storage, new FileSystemWorldBackupStore(state)); var refused = false;
                var request = shape == "root" ? new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, "data", Request(ResourceOutputFormat.DirectoryPack).Languages, new(34, 0)) : Request(ResourceOutputFormat.ZipPack);
                try { _ = service.Build(request); }
                catch (Exception error) when (error is ArgumentException or NotSupportedException or CodecException or IOException) { refused = true; }
                Require(refused && !File.Exists(Path.Combine(path, "resources.zip")), "actual gameplay scope cannot be repurposed even through nested world metadata: " + shape);
            }
            finally { Directory.Delete(world, true); Directory.Delete(state, true); }
        }
    }
    public static void ScopeChangesRefuseApplyAndDisposeEveryCandidate()
    {
        var world = ScratchDirectory.CreateChild(); var root = Path.Combine(world, "data"); Directory.CreateDirectory(root); var state = ScratchDirectory.CreateChild();
        PreparedWorldPlan? prepared = null;
        try
        {
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects")); var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
            var service = new ResourceOutputService(inventory, storage, objects); var plan = service.Build(Request(ResourceOutputFormat.DirectoryPack));
            prepared = service.Prepare(plan, true); var candidates = prepared.Manifest.Artifacts.Select(candidate => candidate.CandidatePath!).ToArray();
            Level(world, false); var refused = false;
            try { _ = new WorldApplyEngine(inventory, storage, objects, journals).Apply(prepared, true); }
            catch (ArtifactConflictException) { refused = true; }
            Require(refused && candidates.All(path => !File.Exists(path)) && !File.Exists(Path.Combine(root, "Translations/pack.mcmeta")), "late scope refusal must preserve data and dispose all engine-owned candidates");
        }
        finally { prepared?.Dispose(); Directory.Delete(world, true); Directory.Delete(state, true); }
    }
    public static void FrozenMergePreservesOpaqueBytesAndConflictingValuesNeverOverwrite()
    {
        foreach (var inputZip in new[] { false, true })
        foreach (var outputFormat in new[] { ResourceOutputFormat.ZipPack, ResourceOutputFormat.DirectoryPack })
        {
            var root = ScratchDirectory.CreateChild(); var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                const string metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep\"},\"opaque\":7}";
                var files = new Dictionary<string, byte[]> { ["pack.mcmeta"] = Encoding.UTF8.GetBytes(metadata), ["assets/test/textures/opaque.bin"] = [0, 255, 13], ["assets/minecraft/lang/zh_tw.json"] = "{ \"keep\" : \"50% raw\" }"u8.ToArray() };
                foreach (var file in files) Put(source, file.Key, file.Value);
                if (inputZip) Put(source, "input.zip", ZipArchiveValidation.AddEntries([0x50, 0x4b, 5, 6, .. new byte[18]], files.Select(file => (file.Key, file.Value))));
                var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
                var request = new ResourceInputService(objects).Capture(Request(outputFormat), new WorldInventory(source, World), inputZip ? new(World, "input.zip", ArtifactKind.Archive) : null);
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World); var service = new ResourceOutputService(inventory, storage, objects);
                var engine = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals")));
                var plan = service.Build(request); Require(engine.Apply(service.Prepare(plan, true), true).Success, "frozen ZIP/directory input uses the same full prepare/apply path");
                byte[] Read(string path)
                {
                    if (outputFormat == ResourceOutputFormat.DirectoryPack) return File.ReadAllBytes(Path.Combine(root, "Translations", path));
                    using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(root, "resources.zip")));
                    using var stream = zip.GetEntry(path)!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
                }
                Require(Read("pack.mcmeta").SequenceEqual(files["pack.mcmeta"]) && Read("assets/test/textures/opaque.bin").SequenceEqual(files["assets/test/textures/opaque.bin"]), "unknown metadata fields and opaque bytes are preserved exactly");
                using var language = BoundedJson.ReadDecoded(Read("assets/minecraft/lang/zh_tw.json"), DecodeLimits.Default);
                Require(NativeJsonStrings.Read(language.RootElement.GetProperty("keep")) == "50% raw", "source language patterns are retained without double percent adaptation");
                var conflict = new ResourceGeneration(World, outputFormat, request.Destination, [new("zh_tw", [new("map.test", "Different")])], request.PackFormat);
                var before = request.Outputs.ToDictionary(output => output.RelativePath, output => File.ReadAllBytes(Path.Combine(root, output.RelativePath)));
                var refused = false; try { _ = service.Build(conflict); } catch (EditRejectedException) { refused = true; }
                Require(refused && before.All(file => File.ReadAllBytes(Path.Combine(root, file.Key)).SequenceEqual(file.Value)), "existing claimed keys conflict and never become overwrite grants");
            }
            finally { Directory.Delete(root, true); Directory.Delete(source, true); Directory.Delete(state, true); }
        }
    }
    public static void KnownResourceScopesAllowAndUnknownWorldMetadataRefuses()
    {
        foreach (var modern in new[] { false, true })
        {
            var world = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Level(world, modern); var root = Path.Combine(world, "resourcepacks"); Directory.CreateDirectory(root);
                var service = new ResourceOutputService(new WorldInventory(root, World), new ArtifactStorage(root, World), new FileSystemWorldBackupStore(state));
                Require(service.Build(Request(ResourceOutputFormat.ZipPack)).Outputs.Length == 1, "explicit resourcepacks scope works for each known world layout");
                Put(world, "level.dat", [9]); var refused = false;
                try { _ = service.Build(Request(ResourceOutputFormat.ZipPack)); } catch (CodecException) { refused = true; }
                Require(refused && !File.Exists(Path.Combine(root, "resources.zip")), "malformed world ancestor cannot be classified as a safe external output");
            }
            finally { Directory.Delete(world, true); Directory.Delete(state, true); }
        }
    }
    public static void InterruptedDirectoryGenerationRecoversWithTheOriginalEngine()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects")); var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
            var interrupted = new InterruptedStorage(storage); var service = new ResourceOutputService(inventory, interrupted, objects);
            var plan = service.Build(Request(ResourceOutputFormat.DirectoryPack));
            var result = new WorldApplyEngine(inventory, interrupted, objects, journals).Apply(service.Prepare(plan, true), true);
            Require(!result.Success && result.Results.Single().Outcome == OperationOutcome.RecoveryRequired && plan.Outputs.Count(output => File.Exists(Path.Combine(root, output.Artifact.RelativePath))) == 1, "one committed file plus one injected I/O failure remains durable recovery-required");
            var fresh = new WorldApplyEngine(new WorldInventory(root, World), new ArtifactStorage(root, World), objects, journals);
            Require(fresh.Recover(plan.PlanHash, true).Success, "fresh engine recovers all promised resource files from immutable postimages");
            Require(fresh.Recover(plan.PlanHash, true, rollback: true).Results.Single().Outcome == OperationOutcome.RolledBack, "the same original journal restores all resource preimages");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    private sealed class InterruptedStorage(IArtifactStorage inner) : IArtifactStorage
    {
        private int commits;
        public Hash256? AccessBinding => inner.AccessBinding;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes) => new Candidate(inner.Stage(artifact, preimage, bytes), this);
        private sealed class Candidate(IArtifactCandidate inner, InterruptedStorage owner) : IArtifactCandidate
        {
            public string CandidatePath => inner.CandidatePath;
            public ArtifactImage Preimage => inner.Preimage;
            public ArtifactImage Postimage => inner.Postimage;
            public void Flush() => inner.Flush();
            public void Verify(Action<Stream> verify) => inner.Verify(verify);
            public void Commit() { if (++owner.commits == 2) throw new IOException("Injected second resource commit failure"); inner.Commit(); }
            public void Dispose() => inner.Dispose();
        }
    }
    public static void AWorldMarkerAppearingBetweenCommitsCannotBypassRecoveryAccounting()
    {
        var world = ScratchDirectory.CreateChild(); var root = Path.Combine(world, "data"); Directory.CreateDirectory(root); var state = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects")); var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
            var changing = new ScopeChangingStorage(storage, () => Level(world, false)); var service = new ResourceOutputService(inventory, changing, objects);
            var plan = service.Build(Request(ResourceOutputFormat.DirectoryPack));
            var result = new WorldApplyEngine(inventory, changing, objects, journals).Apply(service.Prepare(plan, true), true);
            Require(!result.Success && !result.DependenciesComplete && result.Results.Single().Outcome == OperationOutcome.RecoveryRequired
                && plan.Outputs.Count(output => File.Exists(Path.Combine(root, output.Artifact.RelativePath))) == 1, "late semantic scope refusal preserves durable partial-write accounting and stops before a second commit");
        }
        finally { Directory.Delete(world, true); Directory.Delete(state, true); }
    }
    private sealed class ScopeChangingStorage(IArtifactStorage inner, Action change) : IArtifactStorage
    {
        private readonly Action change = change;
        private int commits;
        internal int Commits => commits;
        public Hash256? AccessBinding => inner.AccessBinding;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes) => new Candidate(inner.Stage(artifact, preimage, bytes), this);
        private sealed class Candidate(IArtifactCandidate inner, ScopeChangingStorage owner) : IArtifactCandidate
        {
            public string CandidatePath => inner.CandidatePath;
            public ArtifactImage Preimage => inner.Preimage;
            public ArtifactImage Postimage => inner.Postimage;
            public void Flush() => inner.Flush();
            public void Verify(Action<Stream> verify) => inner.Verify(verify);
            public void Commit() { inner.Commit(); if (++owner.commits == 1) owner.change(); }
            public void Dispose() => inner.Dispose();
        }
    }
    public static void DirectoryDependenciesAndNewMembersAreRecheckedBeforeEachCommit()
    {
        foreach (var change in new[] { "dependency", "new-file", "empty-gameplay-directory" })
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Put(root, "Translations/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Original\"}}"u8.ToArray());
                Put(root, "Translations/assets/test/opaque.bin", [1]);
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
                var changing = new ScopeChangingStorage(storage, () =>
                {
                    if (change == "dependency") Put(root, "Translations/assets/test/opaque.bin", [9]);
                    else if (change == "new-file") Put(root, "Translations/assets/test/new.bin", [9]);
                    else Directory.CreateDirectory(Path.Combine(root, "Translations/data"));
                });
                var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects")); var journals = new FileSystemApplyJournalStore(Path.Combine(state, "journals"));
                var service = new ResourceOutputService(inventory, changing, objects); var plan = service.Build(Request(ResourceOutputFormat.DirectoryPack));
                var result = new WorldApplyEngine(inventory, changing, objects, journals).Apply(service.Prepare(plan, true), true);
                Require(!result.Success && changing.Commits == 1, "an external directory dependency/catalog change stops before the next commit: " + change);
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void ApplyImageVerificationDoesNotMaterializeEachRead(string? artifactRoot)
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var opaque = new byte[4 * 1024 * 1024]; opaque[0] = 7; opaque[^1] = 31;
            const string opaquePath = "Translations/assets/test/opaque.bin";
            Put(root, opaquePath, opaque);
            Put(root, "Translations/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Retained\"}}"u8.ToArray());
            var request = new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, "Translations",
                Enumerable.Range(0, 8).Select(index => new ResourceLanguage($"zz_{index:D2}", [new("map.test", "Value")])), new(34, 0));
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
            var engine = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals")));
            var service = new ResourceOutputService(inventory, storage, objects);
            using var prepared = service.Prepare(service.Build(request), true);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var receipt = engine.Apply(prepared, true);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (artifactRoot is not null)
            {
                Directory.CreateDirectory(artifactRoot);
                File.WriteAllText(Path.Combine(artifactRoot, "apply-image-allocation.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { allocatedBytes = bytes, retainedBytes = opaque.Length, outputs = prepared.Manifest.Artifacts.Length }));
            }
            Require(receipt.Success && receipt.Results.Single().Proofs.Length == 9
                && File.ReadAllBytes(Path.Combine(root, opaquePath)).AsSpan().SequenceEqual(opaque),
                "durable apply must prove every output and preserve its exact retained dependency");
            Require(bytes < 48L * 1024 * 1024, $"Apply allocated {bytes:N0} bytes for a 4 MiB retained dependency across 9 verified outputs");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void LegacyWorldResourceLocationsRemainAvailableWithoutGameplayAuthority()
    {
        foreach (var format in new[] { ResourceOutputFormat.ZipPack, ResourceOutputFormat.LanguageFiles, ResourceOutputFormat.DirectoryPack })
        {
            var world = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Level(world, false); var original = File.ReadAllBytes(Path.Combine(world, "level.dat"));
                var root = format == ResourceOutputFormat.LanguageFiles ? Path.Combine(world, ".nbtcli_translate") : world; Directory.CreateDirectory(root);
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World);
                var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects")); var service = new ResourceOutputService(inventory, storage, objects);
                var plan = service.Build(Request(format));
                var result = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals"))).Apply(service.Prepare(plan, true), true);
                Require(result.Success && File.ReadAllBytes(Path.Combine(world, "level.dat")).SequenceEqual(original), "legal world resource output preserves metadata/gameplay bytes: " + format);
            }
            finally { Directory.Delete(world, true); Directory.Delete(state, true); }
        }
    }
    public static void NestedOpaqueMetadataUsesItsDeclaredKindDuringDirectoryMerge()
    {
        var root = ScratchDirectory.CreateChild(); var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var files = new Dictionary<string, byte[]> { ["pack.mcmeta"] = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep\"}}"u8.ToArray(), ["assets/test/pack.mcmeta"] = [0, 255, 9] };
            foreach (var file in files) { Put(source, file.Key, file.Value); Put(root, "Translations/" + file.Key, file.Value); }
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
            var request = new ResourceInputService(objects).Capture(Request(ResourceOutputFormat.DirectoryPack), new WorldInventory(source, World));
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World); var service = new ResourceOutputService(inventory, storage, objects);
            var plan = service.Build(request); var opaque = plan.Outputs.Single(output => output.Artifact.RelativePath == "Translations/assets/test/pack.mcmeta").Artifact;
            Require(opaque.Kind == ArtifactKind.Text && plan.Preconditions.Single(input => input.Artifact.RelativePath == opaque.RelativePath).Artifact == opaque, "nested opaque mcmeta keeps the recipe's Text kind in both output and preimage");
            Require(new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals"))).Apply(service.Prepare(plan, true), true).Success
                && File.ReadAllBytes(Path.Combine(root, opaque.RelativePath)).SequenceEqual(files["assets/test/pack.mcmeta"]), "directory merge preserves nested opaque metadata bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(source, true); Directory.Delete(state, true); }
    }
}
