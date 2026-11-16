using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class MissingOutputDirectoryTests
{
    public static void ParentsAppearOnlyDuringExplicitCommit()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var artifact = new ArtifactAddress(World, "new/deep/file.json", ArtifactKind.Json);
            var store = new ArtifactStorage(root, World); var parent = Path.Combine(root, "new");
            using (var candidate = store.Stage(artifact, ArtifactImage.Absent, "{}"u8.ToArray()))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                Require(!Directory.Exists(parent) && File.Exists(candidate.CandidatePath), "staging uses an existing guarded ancestor and creates no formal parent");
            }
            Require(!Directory.Exists(parent) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "abandoned staging leaves no new parents or candidates");
            using (var deletion = store.StageDelete(artifact, ArtifactImage.Absent)) { deletion.Flush(); deletion.Verify(_ => { }); deletion.Commit(); }
            Require(!Directory.Exists(parent), "already absent delete never creates directories");
            using (var candidate = store.Stage(artifact, ArtifactImage.Absent, "{}"u8.ToArray()))
            {
                candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
                Require(store.Read(artifact).SequenceEqual("{}"u8.ToArray()), "explicit commit creates guarded parents and atomically publishes the verified output");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ReparseOrExternallyCreatedTargetsAreNotAdopted()
    {
        var root = ScratchDirectory.CreateChild(); var outside = ScratchDirectory.CreateChild(); var link = Path.Combine(root, "new");
        try
        {
            var artifact = new ArtifactAddress(World, "new/file.json", ArtifactKind.Json); var store = new ArtifactStorage(root, World);
            Put(outside, "file.json", [5]);
            using (var candidate = store.Stage(artifact, ArtifactImage.Absent, "{}"u8.ToArray()))
            {
                candidate.Flush(); candidate.Verify(_ => { }); CandidatePathTests.CreateJunction(link, outside);
                try { candidate.Commit(); throw new InvalidOperationException("Expected parent reparse rejection"); }
                catch (IOException error) { Require(error.Message == "Directory reparse points are not allowed", "parent reparse must be rejected before reading its outside target image"); }
                Require(File.ReadAllBytes(Path.Combine(outside, "file.json")).SequenceEqual(new byte[] { 5 }), "a parent created as a junction after staging cannot redirect commit");
            }
            Directory.Delete(link);
            using (var candidate = store.Stage(artifact, ArtifactImage.Absent, "{}"u8.ToArray()))
            {
                candidate.Flush(); candidate.Verify(_ => { }); Put(root, artifact.RelativePath, [7]); Reject<ArtifactConflictException>(candidate.Commit);
                Require(File.ReadAllBytes(Path.Combine(root, artifact.RelativePath)).SequenceEqual(new byte[] { 7 }), "external creation after stage is never overwritten");
            }
        }
        finally
        {
            if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link);
            Directory.Delete(root, true); Directory.Delete(outside, true);
        }
    }
    public static void LanguagePlansCanDeclareAbsentNamespaceParents()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string component = "{\"text\":\"Hello\"}"; LegacyImportMappingTests.BossWorld(root, componentJson: component);
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}"u8.ToArray());
            Put(root, "resourcepacks/client/assets/existing/lang/en_us.json", "{}"u8.ToArray());
            var inventory = new WorldInventory(root, World); var snapshot = LegacyImportMappingTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(row => row.Text == "Hello" && row.Address.Artifact.RelativePath == "level.dat").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(3953)!; var language = new ArtifactAddress(World, "resourcepacks/client/assets/test/lang/zh_tw.json", ArtifactKind.Json);
            var operation = new KeyConversionPatch("missing-namespace", new(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root), new NbtString(component),
                KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated"), new(language, null, "zh_tw", "test.new", "Translated")));
            var before = File.ReadAllBytes(Path.Combine(root, "level.dat"));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            Require(!Directory.Exists(Path.Combine(root, "resourcepacks/client/assets/test")), "preview declares parents without creating them");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                Require(prepared.Manifest.Artifacts.Length == 2 && !Directory.Exists(Path.Combine(root, "resourcepacks/client/assets/test")), "complete language staging also leaves formal directories absent");
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(before) && !Directory.Exists(Path.Combine(root, "resourcepacks/client/assets/test")), "Dispose preserves the original world and absence of language parents");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void InvalidParentsAndMissingWorldRootAreRejected()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new ArtifactStorage(root, World); Put(root, "file", [1]);
            Reject<IOException>(() => store.Stage(new(World, "file/child.json", ArtifactKind.Json), ArtifactImage.Absent, "{}"u8.ToArray()));
            Reject<ArgumentException>(() => store.Stage(new(World, "new/CON/file.json", ArtifactKind.Json), ArtifactImage.Absent, "{}"u8.ToArray()));
            Reject<FileNotFoundException>(() => new ArtifactStorage(Path.Combine(root, "not-a-world"), World).Stage(new(World, "new/file.json", ArtifactKind.Json), ArtifactImage.Absent, "{}"u8.ToArray()));
            Require(!Directory.Exists(Path.Combine(root, "new")) && !Directory.Exists(Path.Combine(root, "not-a-world"))
                && File.ReadAllBytes(Path.Combine(root, "file")).SequenceEqual(new byte[] { 1 }), "parent creation never adopts a missing world root, reserved directory or file-as-parent");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void RestoreCanStageUnderAbsentParents()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var artifact = new ArtifactAddress(World, "gone/file.dat", ArtifactKind.Nbt); Put(root, artifact.RelativePath, [10, 255]);
            var inventory = new WorldInventory(root, World);
            var backup = new WorldBackupService(inventory, new FileSystemWorldBackupStore(state)).Create([new(artifact, ArtifactImage.Present(Hash256.Compute(new byte[] { 10, 255 })))]);
            File.Delete(Path.Combine(root, artifact.RelativePath)); Directory.Delete(Path.Combine(root, "gone"));
            var operation = new RestorePatch("restore", new(backup.ManifestHash, backup.Manifest.Artifacts, new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = ArtifactImage.Absent }));
            var plan = new WorldPlanBuilder(inventory).Build(backup, [operation]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(backup, plan, true))
                Require(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!).SequenceEqual(new byte[] { 10, 255 }) && !Directory.Exists(Path.Combine(root, "gone")), "raw restore stages exact corrupt bytes in a guarded ancestor without creating absent parents");
            Require(!Directory.Exists(Path.Combine(root, "gone")) && !Directory.EnumerateFiles(root, "*.candidate").Any(), "abandoned raw restore preserves missing parents and removes its candidate");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void PlannedFilesCannotAlsoBeParentDirectories()
    {
        foreach (var parent in new[] { "z-generated.zip", "Z-GENERATED.zip" })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                const string component = "[{\"text\":\"First\"},{\"text\":\"Second\"}]";
                LegacyImportMappingTests.BossWorld(root, componentJson: component);
                var snapshot = LegacyImportMappingTests.Query(root, cache); var inventory = new WorldInventory(root, World);
                var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(row => row.Address.Artifact.RelativePath == "level.dat").Select(row => row.ToSlot("en_us", "zh_tw")).ToArray();
                PatchOperation[] Operations(string firstPath)
                {
                    var profile = MinecraftProfiles.ForDataVersion(3953)!;
                    return new[] { firstPath, "z-generated.zip/inner.zip" }.Select((path, index) =>
                    {
                        var slot = slots.Single(item => item.SourceText == (index == 0 ? "First" : "Second"));
                        var decoded = new TypedPath(slot.Address.ComponentPath!.Segments.SkipLast(1));
                        var address = new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: decoded);
                        var outer = new ArtifactAddress(World, path, ArtifactKind.Archive); var language = new ArtifactAddress(World, path, ArtifactKind.Archive, "assets/test/lang/zh_tw.json");
                        return (PatchOperation)new KeyConversionPatch("conversion-" + index, new(address, new NbtString(component), KeyConversionRule.EncodedTextToTranslate,
                            profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated" + index), new(language, outer, "zh_tw", "test.key" + index, "Translated" + index)));
                    }).ToArray();
                }
                var before = File.ReadAllBytes(Path.Combine(root, "level.dat")); var builder = new WorldPlanBuilder(inventory);
                var control = builder.Build(snapshot, Operations("z-generated.zip-extra.zip"));
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, control, true))
                    Require(prepared.Manifest.Artifacts.Length == 3, "a textual prefix without a separator remains a valid three-output control");
                Reject<ArgumentException>(() => builder.Build(snapshot, Operations(parent)));
                Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(before) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "file/parent conflict fails before staging or partial commits for either Windows casing");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
    public static void PhysicalInputAndOutputAncestorsConflictInTheSharedPlan()
    {
        static PatchPlan Plan(string[] paths, string? extraInput = null)
        {
            var artifacts = paths.Select(path => new ArtifactAddress(World, path, ArtifactKind.Archive)).ToArray();
            var before = ArtifactImage.Present(PlanTests.Preimage); var after = ArtifactImage.Present(PlanTests.Postimage);
            var inputs = artifacts.Select(artifact => new ArtifactPrecondition(artifact, before));
            if (extraInput is not null) inputs = inputs.Append(new(new(World, extraInput, ArtifactKind.Text), ArtifactImage.Absent));
            var restore = new RestorePatch("restore", new(PlanTests.Preimage, artifacts.Select(artifact => new BackupArtifact(artifact, after)), artifacts.ToDictionary(artifact => artifact, _ => before)));
            return new(World, PlanTests.Snapshot, "raw-v1", "raw-v1", [restore], inputs, artifacts.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, ["raw"])), []);
        }
        Require(Plan(["a", "a-b/child"]).Outputs.Length == 2, "shared validation compares path components, not bare textual prefixes");
        Reject<ArgumentException>(() => Plan(["a", "a-b", "a/child"]));
        Reject<ArgumentException>(() => Plan(["A", "a/child"]));
        Reject<ArgumentException>(() => Plan(["a/child"], extraInput: "a"));
    }
}
