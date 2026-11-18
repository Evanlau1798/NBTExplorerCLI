using Next.Application;
using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CandidateIoTests
{
    private static ArtifactAddress Artifact(string path = "level.dat") => new(World, path, ArtifactKind.Nbt);
    public static void CandidateLifecycleStreamsExactImages(string? artifactRoot)
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat");
            var replacement = new byte[16 * 1024 * 1024]; replacement[0] = 17; replacement[^1] = 31;
            File.WriteAllBytes(path, new byte[replacement.Length]);
            var storage = new Infrastructure.ArtifactStorage(root, World);
            var preimage = ArtifactImage.Present(Hash256.Compute(File.ReadAllBytes(path)));
            var postimage = ArtifactImage.Present(Hash256.Compute(replacement));
            var before = GC.GetAllocatedBytesForCurrentThread();
            using (var candidate = storage.Stage(Artifact(), preimage, replacement))
            {
                candidate.Flush();
                candidate.Verify(stream =>
                {
                    Require(stream.Length == replacement.Length && stream.ReadByte() == 17, "persisted candidate stream begins at the exact first byte");
                    Require(stream.Seek(-1, SeekOrigin.End) == replacement.Length - 1 && stream.ReadByte() == 31, "candidate verifier can seek to the last persisted byte");
                });
                candidate.Commit();
                Require(candidate.Postimage == postimage, "staged identity and committed image remain bound");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(Hash256.Compute(storage.Read(Artifact())) == postimage.Hash, "committed artifact is the exact authorized image");
            if (artifactRoot is not null)
            {
                Directory.CreateDirectory(artifactRoot);
                File.WriteAllText(Path.Combine(artifactRoot, "candidate-hash-allocation.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { allocatedBytes = allocated, artifactBytes = replacement.Length }));
            }
            Require(allocated < 8L * 1024 * 1024, $"Candidate stage, flush, verify and commit allocated {allocated:N0} bytes");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void ExplicitLifecycle()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); byte[] before = [1, 2, 3], after = [4, 5, 6];
            File.WriteAllBytes(path, before); var store = new Infrastructure.ArtifactStorage(root, World);
            var preimage = ArtifactImage.Present(Hash256.Compute(before));
            Require(store.Read(Artifact()).SequenceEqual(before), "bounded read returns source bytes");
            using (var pending = store.Stage(Artifact(), preimage, after))
            {
                Require(File.Exists(pending.CandidatePath) && Path.GetDirectoryName(pending.CandidatePath) == root, "same-directory candidate is distinct from source");
                Require(File.ReadAllBytes(path).SequenceEqual(before), "stage never changes source");
                Reject<InvalidOperationException>(pending.Commit);
                pending.Flush(); Reject<InvalidOperationException>(pending.Commit);
                pending.Verify(stream => { Require(stream.ReadByte() == 4, "verify reads persisted candidate"); });
                Require(File.ReadAllBytes(path).SequenceEqual(before), "flush and verify do not commit");
            }
            Require(File.ReadAllBytes(path).SequenceEqual(before) && Directory.GetFiles(root, "*.candidate").Length == 0, "Dispose only removes uncommitted candidate");
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
                Require(candidate.Postimage == ArtifactImage.Present(Hash256.Compute(after)) && store.Read(Artifact()).SequenceEqual(after), "explicit commit requires exact disk readback");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void StaleCorruptAndLocked()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); byte[] before = [1, 2, 3], after = [4, 5, 6];
            File.WriteAllBytes(path, before); var store = new Infrastructure.ArtifactStorage(root, World);
            var preimage = ArtifactImage.Present(Hash256.Compute(before));
            Reject<ArtifactConflictException>(() => store.Stage(Artifact(), ArtifactImage.Absent, after));
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); File.WriteAllBytes(candidate.CandidatePath, [7, 8, 9]);
                Reject<ArtifactConflictException>(() => candidate.Verify(_ => { }));
                Reject<InvalidOperationException>(candidate.Commit);
                Require(File.ReadAllBytes(path).SequenceEqual(before), "corrupt candidate cannot truncate source");
            }
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                File.WriteAllBytes(path, [7, 8, 9]); Reject<ArtifactConflictException>(candidate.Commit);
                Require(File.ReadAllBytes(path).SequenceEqual(new byte[] { 7, 8, 9 }), "external change is never overwritten");
            }
            File.WriteAllBytes(path, before);
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                File.Delete(path); Reject<ArtifactConflictException>(candidate.Commit);
            }
            File.WriteAllBytes(path, before);
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) Reject<IOException>(candidate.Commit);
                Require(File.ReadAllBytes(path).SequenceEqual(before), "actual locked file failure preserves source");
            }
            Require(Directory.GetFiles(root, "*.candidate").Length == 0, "failed candidates cleanly release");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void SingleWriterAndReadOnly()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); byte[] before = [1, 2, 3], after = [4, 5, 6];
            File.WriteAllBytes(path, before); var store = new Infrastructure.ArtifactStorage(root, World);
            var preimage = ArtifactImage.Present(Hash256.Compute(before));
            using (var first = store.Stage(Artifact(), preimage, after))
            {
                using var related = store.Stage(Artifact("other.dat"), ArtifactImage.Absent, after);
                Reject<IOException>(() => { using var second = new Infrastructure.ArtifactStorage(root, World).Stage(Artifact(), preimage, after); });
                related.Flush(); related.Verify(_ => { });
                Require(File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(Path.Combine(root, "other.dat")), "one writer can stage all outputs before any commit");
            }
            using (var candidate = store.Stage(Artifact(), preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                File.SetAttributes(path, FileAttributes.ReadOnly);
                Reject<IOException>(candidate.Commit);
                File.SetAttributes(path, FileAttributes.Normal);
                Require(File.ReadAllBytes(path).SequenceEqual(before), "read-only replace failure preserves source");
            }
            var created = Artifact("new.dat");
            using (var candidate = store.Stage(created, ArtifactImage.Absent, after))
            {
                candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
                Require(store.Read(created).SequenceEqual(after), "expected-absent create is explicit and verified");
            }
        }
        finally
        {
            var path = Path.Combine(root, "level.dat"); if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
    internal static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
