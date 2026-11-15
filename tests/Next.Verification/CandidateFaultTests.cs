using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.CandidateIoTests;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CandidateFaultTests
{
    public static void StagingFailuresPreserveSource()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); byte[] before = [1, 2, 3], after = [4, 5, 6];
            File.WriteAllBytes(path, before); var artifact = new ArtifactAddress(World, "level.dat", ArtifactKind.Nbt);
            var preimage = ArtifactImage.Present(Hash256.Compute(before));
            foreach (var fault in new[] { "disk-full", "short-write", "flush", "verify" })
            {
                var store = new ArtifactStorage(root, World, new FaultIo(fault));
                if (fault == "disk-full") Reject<IOException>(() => { using var candidate = store.Stage(artifact, preimage, after); });
                else
                {
                    using var candidate = store.Stage(artifact, preimage, after);
                    if (fault == "flush") Reject<IOException>(candidate.Flush);
                    else
                    {
                        candidate.Flush();
                        if (fault == "short-write") Reject<ArtifactConflictException>(() => candidate.Verify(_ => { }));
                        else Reject<InvalidDataException>(() => candidate.Verify(_ => throw new InvalidDataException("parser rejected candidate")));
                    }
                    Reject<InvalidOperationException>(candidate.Commit);
                }
                Require(File.ReadAllBytes(path).SequenceEqual(before) && Directory.GetFiles(root, "*.candidate").Length == 0, "all staging failure paths leave source and cleanup intact");
            }
            foreach (var fault in new[] { "replace-before", "replace-after", "readback-corrupt" })
            {
                var store = new ArtifactStorage(root, World, new FaultIo(fault));
                using (var candidate = store.Stage(artifact, preimage, after))
                {
                    candidate.Flush(); candidate.Verify(_ => { });
                    Reject<ArtifactCommitException>(candidate.Commit); Reject<InvalidOperationException>(candidate.Commit);
                }
                Require(File.ReadAllBytes(path).SequenceEqual(fault == "replace-before" ? before : fault == "readback-corrupt" ? new byte[] { 0 } : after), "ambiguous commit is never retried or called unchanged");
                foreach (var pending in Directory.GetFiles(root, "*.candidate")) File.Delete(pending);
                File.WriteAllBytes(path, before);
            }
            var blockedWrite = new FaultIo("external-content-write");
            using (var candidate = new ArtifactStorage(root, World, blockedWrite).Stage(artifact, preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { }); candidate.Commit();
                Require(blockedWrite.ExternalWriteBlocked, "source content writer must be excluded across final hash check and OS replace");
            }
            File.WriteAllBytes(path, before);
            using (var candidate = new ArtifactStorage(root, World, new FaultIo("second-flush")).Stage(artifact, preimage, after))
            {
                candidate.Flush(); candidate.Verify(_ => { });
                Reject<IOException>(candidate.Flush);
                Reject<InvalidOperationException>(() => candidate.Verify(_ => { }));
                Reject<InvalidOperationException>(candidate.Commit);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private sealed class FaultIo(string fault) : ArtifactFileIo
    {
        public bool ExternalWriteBlocked { get; private set; }
        private int flushes;
        public override void Write(FileStream output, ReadOnlyMemory<byte> bytes)
        {
            if (fault is "disk-full" or "short-write")
            {
                output.Write(bytes.Span[..1]);
                if (fault == "disk-full") throw new IOException("Injected disk full after persisted prefix", unchecked((int)0x80070070));
                return;
            }
            base.Write(output, bytes);
        }
        public override void Flush(FileStream output)
        {
            if (fault == "flush" || fault == "second-flush" && ++flushes == 2) throw new IOException("Injected flush failure");
            base.Flush(output);
        }
        public override void Replace(string candidate, string target, bool absent)
        {
            if (fault == "external-content-write")
            {
                try { File.WriteAllBytes(target, [99]); } catch (IOException) { ExternalWriteBlocked = true; }
            }
            if (fault == "replace-before") throw new IOException("Injected before OS replace");
            base.Replace(candidate, target, absent);
            if (fault == "replace-after") throw new IOException("Injected after OS replace");
            if (fault == "readback-corrupt") File.WriteAllBytes(target, [0]);
        }
    }
}
