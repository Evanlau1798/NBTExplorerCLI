using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class WorldRestorePreparationTests
{
    private static ArtifactImage Present(params byte[] bytes) => ArtifactImage.Present(Hash256.Compute(bytes));
    private static readonly ArtifactAddress Broken = new(World, "a.dat", ArtifactKind.Nbt);
    private static readonly ArtifactAddress Empty = new(World, "b.bin", ArtifactKind.Text);
    private static readonly ArtifactAddress Removed = new(World, "c.json", ArtifactKind.Json);
    private static VerifiedBackup Backup(string root, string state)
    {
        Put(root, "a.dat", [10, 255, 8]); Put(root, "b.bin", []);
        return new WorldBackupService(new WorldInventory(root, World), new FileSystemWorldBackupStore(state))
            .Create([new(Broken, Present(10, 255, 8)), new(Empty, Present()), new(Removed, ArtifactImage.Absent)]);
    }
    private static RestorePatch[] Requests(VerifiedBackup backup)
    {
        var images = new Dictionary<ArtifactAddress, ArtifactImage> { [Broken] = Present(99), [Empty] = ArtifactImage.Absent, [Removed] = Present(1) };
        return [new("restore", new(backup.ManifestHash, backup.Manifest.Artifacts, images))];
    }
    private static void Damage(string root)
    {
        Put(root, "a.dat", [99]); File.Delete(Path.Combine(root, "b.bin")); Put(root, "c.json", [1]);
    }
    private static void Unchanged(string root)
        => Require(File.ReadAllBytes(Path.Combine(root, "a.dat")).SequenceEqual(new byte[] { 99 })
            && !File.Exists(Path.Combine(root, "b.bin")) && File.ReadAllBytes(Path.Combine(root, "c.json")).SequenceEqual(new byte[] { 1 }), "preview, staging and Dispose preserve every damaged current image");
    public static void RawRestorePreparesAllImagesWithoutScanningOrParsing()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var backup = Backup(root, state); Damage(root); var inventory = new WorldInventory(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(backup, Requests(backup));
            Require(plan.Outputs.Length == 3 && plan.Outputs.Single(output => output.Artifact == Removed).Intent == OutputIntent.Delete, "restore preview declares exact raw writes and deletion without a Minecraft snapshot");
            Require(!Directory.EnumerateFiles(root, "*.candidate").Any(), "preview creates no staged or formal files"); Unchanged(root);
            var service = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));
            Reject<ArgumentException>(() => service.Prepare(backup, plan, false));
            using (var prepared = service.Prepare(backup, plan, true))
            {
                Require(prepared.Manifest.Artifacts.Length == 3 && prepared.Manifest.PlanHash == plan.PlanHash, "all restore targets have one frozen bound manifest");
                foreach (var artifact in prepared.Manifest.Artifacts)
                {
                    var expected = backup.Manifest.Artifacts.Single(row => row.Artifact == artifact.Artifact).Image;
                    Require(artifact.Postimage == expected && artifact.Checks.All(check => check.Passed), "restore checks bind the actual verified backup target image");
                    if (expected.ExpectedAbsent) Require(artifact.CandidatePath is null, "absence is a delete intent, never a zero-byte replacement");
                    else Require(File.ReadAllBytes(artifact.CandidatePath!).SequenceEqual(backup.ReadArtifact(artifact.Artifact)!), "corrupt NBT and empty-file backup bytes stage without parsing");
                }
                Unchanged(root);
            }
            Unchanged(root); Require(!Directory.EnumerateFiles(root, "*.candidate").Any(), "Dispose removes every raw/deletion staging artifact without committing");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    private sealed class FaultStorage(IArtifactStorage inner, string mode) : IArtifactStorage
    {
        private IArtifactCandidate? first;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
        {
            if (first is not null && mode == "fail") throw new IOException("Injected later restore stage failure");
            var next = inner.Stage(artifact, preimage, bytes);
            if (first is not null) File.WriteAllBytes(first.CandidatePath, [7]); else first = next;
            return next;
        }
        public IArtifactCandidate StageDelete(ArtifactAddress artifact, ArtifactImage preimage) => inner.StageDelete(artifact, preimage);
    }
    public static void ProvenancePreimagesAndWholeSetFailuresAreRejected()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var backup = Backup(root, state); Damage(root); var inventory = new WorldInventory(root, World); var builder = new WorldPlanBuilder(inventory);
            var requests = Requests(backup); var plan = builder.Build(backup, requests);
            var original = requests[0].Restore;
            var parts = original.AuthorizedArtifacts.Select((row, index) => new RestorePatch("part-" + index, new(backup.ManifestHash, [row],
                new Dictionary<ArtifactAddress, ArtifactImage> { [row.Artifact] = original.CurrentImages[row.Artifact] }))).ToArray();
            Require(builder.Build(backup, parts).PlanHash == builder.Build(backup, parts.Reverse()).PlanHash, "multiple explicit restore operations bind deterministically without merging their IDs");
            var subset = builder.Build(backup, [parts[0]]);
            Require(subset.Outputs.Length == 1 && subset.Outputs[0].Artifact == parts[0].Restore.AuthorizedArtifacts[0].Artifact, "manifest membership permits only the explicitly requested subset");
            Reject<ArtifactConflictException>(() => new WorldPlanBuilder(new WorldInventory(root, Guid.NewGuid())).Build(backup, requests));
            var forgedPolicy = new PatchPlan(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, "forged-policy", plan.Operations, plan.Preconditions, plan.Outputs, plan.Dependencies);
            Reject<ArtifactConflictException>(() => new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(backup, forgedPolicy, true));
            Reject<ArtifactConflictException>(() => builder.Build(backup, [new("foreign", new(Hash256.Compute([4]), original.AuthorizedArtifacts, original.CurrentImages))]));
            var forged = original.AuthorizedArtifacts.Select(row => row.Artifact == Broken ? new BackupArtifact(Broken, Present(42)) : row);
            Reject<ArtifactConflictException>(() => builder.Build(backup, [new("forged", new(backup.ManifestHash, forged, original.CurrentImages))]));
            var unlisted = new ArtifactAddress(World, "unlisted.dat", ArtifactKind.Nbt);
            Reject<ArgumentException>(() => builder.Build(backup, [new("extra", new(backup.ManifestHash, [new(unlisted, ArtifactImage.Absent)], new Dictionary<ArtifactAddress, ArtifactImage> { [unlisted] = ArtifactImage.Absent }))]));
            foreach (var mode in new[] { "fail", "tamper" })
            {
                Reject<IOException>(() => { using var rejected = new WorldPlanPreparation(inventory, new FaultStorage(new ArtifactStorage(root, World), mode)).Prepare(backup, plan, true); });
                Unchanged(root); Require(!Directory.EnumerateFiles(root, "*.candidate").Any(), "later failure or earlier candidate tampering cleans the full restore set");
            }
            Put(root, "b.bin", [8]); Reject<ArtifactConflictException>(() => builder.Build(backup, requests));
            Reject<ArtifactConflictException>(() => new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(backup, plan, true)); File.Delete(Path.Combine(root, "b.bin"));
            File.WriteAllBytes(Path.Combine(state, "blob-" + Present(10, 255, 8).Hash!.Hex + ".bin"), [2]);
            Reject<InvalidDataException>(() => new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(backup, plan, true));
            Unchanged(root); Require(!Directory.EnumerateFiles(root, "*.candidate").Any(), "changed verified backup bytes cannot stage or alter the current world");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void DeletionHasAnExplicitVerifiedLifecycle()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, Removed.RelativePath); Put(root, Removed.RelativePath, [1]); var storage = new ArtifactStorage(root, World);
            using (var candidate = storage.StageDelete(Removed, Present(1)))
            {
                Reject<InvalidOperationException>(candidate.Commit); candidate.Flush(); Reject<InvalidOperationException>(candidate.Commit);
                candidate.Verify(_ => { }); Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1 }), "verified deletion has not touched its source");
            }
            Require(File.Exists(path) && !Directory.EnumerateFiles(root, "*.candidate").Any(), "Dispose never deletes the source");
            using (var candidate = storage.StageDelete(Removed, Present(1)))
            {
                candidate.Flush(); candidate.Verify(_ => { }); File.WriteAllBytes(candidate.CandidatePath, [9]);
                Reject<ArtifactConflictException>(candidate.Commit);
                Require(File.Exists(path), "tampered deletion marker cannot authorize a delete");
            }
            using (var candidate = storage.StageDelete(Removed, Present(1)))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Reject<IOException>(candidate.Commit);
            }
            using (var candidate = storage.StageDelete(Removed, Present(1)))
            {
                candidate.Flush(); candidate.Verify(_ => { }); Put(root, Removed.RelativePath, [3]); Reject<ArtifactConflictException>(candidate.Commit);
            }
            Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 3 }), "external third image cannot be deleted"); Put(root, Removed.RelativePath, [1]);
            using (var candidate = storage.StageDelete(Removed, Present(1)))
            {
                candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit(); Require(!File.Exists(path) && candidate.Postimage.ExpectedAbsent, "explicit delete commits only after verification and absence readback");
            }
            using (var candidate = storage.StageDelete(Removed, ArtifactImage.Absent)) { candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit(); }
            Require(!File.Exists(path) && !Directory.EnumerateFiles(root, "*.candidate").Any(), "already absent delete converges without creating an empty file or leaving markers");
            Put(root, Removed.RelativePath, [1]);
            var io = new DeleteFault(path);
            using (var candidate = new ArtifactStorage(root, World, io).StageDelete(Removed, Present(1)))
            {
                candidate.Flush(); candidate.Verify(_ => { }); Reject<ArtifactCommitException>(candidate.Commit);
                Reject<InvalidOperationException>(candidate.Commit);
                Require(io.WriteBlocked && File.ReadAllBytes(path).SequenceEqual(new byte[] { 1 }), "delete excludes a content writer across hash/deletion and preserves an ambiguous-attempt marker");
            }
            foreach (var marker in Directory.EnumerateFiles(root, "*.candidate")) File.Delete(marker);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class DeleteFault(string path) : ArtifactFileIo
    {
        public bool WriteBlocked { get; private set; }
        public override void Delete(FileStream source)
        {
            try { File.WriteAllBytes(path, [9]); } catch (IOException) { WriteBlocked = true; }
            throw new IOException("Injected delete failure before OS deletion");
        }
    }
    public static void DeletionBindsTheCheckedFileIdentity()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, Removed.RelativePath); Put(root, Removed.RelativePath, [1]);
            var io = new DeleteReplacement(path);
            using var candidate = new ArtifactStorage(root, World, io).StageDelete(Removed, Present(1));
            candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
            Require(io.RenameBlocked && !File.Exists(path + ".moved"), "the checked source identity must exclude rename/replacement through the OS deletion boundary");
            Require(!File.Exists(path), "normal deletion still reaches verified absence readback");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class DeleteReplacement(string path) : ArtifactFileIo
    {
        public bool RenameBlocked { get; private set; }
        public override void Delete(FileStream source)
        {
            try { File.Move(path, path + ".moved"); File.WriteAllBytes(path, [9]); }
            catch (IOException) { RenameBlocked = true; }
            base.Delete(source);
        }
    }
}
