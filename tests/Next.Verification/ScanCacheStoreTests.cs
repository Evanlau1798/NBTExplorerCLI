using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class ScanCacheStoreTests
{
    public static void ExactCasAndImmutableShardRepair()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemScanCacheStore(root); byte[] first = [1, 2, 3], second = [4, 5, 6];
            Require(store.ReadManifest() is null, "new owned cache has no published manifest");
            var hash = Hash256.Compute(first); store.WriteShard(hash, first);
            Require(store.ReadShard(hash)!.SequenceEqual(first), "content-addressed shard is verified before readback");
            Reject<ArgumentException>(() => store.WriteShard(hash, second));
            store.PublishManifest(first, null);
            Reject<ArtifactConflictException>(() => store.PublishManifest(second, null));
            Require(store.ReadManifest()!.SequenceEqual(first), "stale expected-absent publish preserves the existing manifest");
            store.PublishManifest(second, hash);
            Require(new FileSystemScanCacheStore(root).ReadManifest()!.SequenceEqual(second), "fresh repository instance observes exact committed manifest");
            File.WriteAllBytes(Path.Combine(root, hash.Hex + ".shard.json"), second);
            Reject<InvalidDataException>(() => store.ReadShard(hash));
            store.WriteShard(hash, first);
            Require(store.ReadShard(hash)!.SequenceEqual(first), "corrupt derived shard is repaired through expected-preimage replacement");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class FlushFailure : ArtifactFileIo
    {
        public override void Flush(FileStream output) => throw new IOException("Injected cache flush failure");
    }
    public static void FailureAndForeignRootPreserveData()
    {
        var root = ScratchDirectory.CreateChild(); var foreign = ScratchDirectory.CreateChild();
        try
        {
            byte[] before = [1, 2, 3], after = [4, 5, 6];
            var store = new FileSystemScanCacheStore(root); store.PublishManifest(before, null);
            var failing = new FileSystemScanCacheStore(root, new FlushFailure());
            Reject<IOException>(() => failing.PublishManifest(after, Hash256.Compute(before)));
            Require(store.ReadManifest()!.SequenceEqual(before) && Directory.GetFiles(root, "*.candidate").Length == 0,
                "failed state flush preserves prior manifest and cleans owned temporary candidate");
            File.WriteAllBytes(Path.Combine(foreign, "manifest.json"), before);
            Reject<IOException>(() => new FileSystemScanCacheStore(foreign));
            Require(Directory.GetFiles(foreign).Length == 1 && File.ReadAllBytes(Path.Combine(foreign, "manifest.json")).SequenceEqual(before),
                "unowned nonempty root is rejected without overwriting user data or adding state files");
        }
        finally { Directory.Delete(root, true); Directory.Delete(foreign, true); }
    }
}
