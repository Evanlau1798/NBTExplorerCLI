using System.Text;
using System.Text.Json;
using System.Reflection;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class WorldBackupTests
{
    public static void MetadataEncodingStopsAtItsOutputBudget()
    {
        static BackupManifest Metadata(char unit)
        {
            var prefix = string.Join('/', Enumerable.Repeat(new string(unit, 100), 100));
            return new(World, Enumerable.Range(0, 1200).Select(index => new BackupArtifact(new(World, prefix + "/f" + index + ".json", ArtifactKind.Json), ArtifactImage.Absent)));
        }
        var control = BackupManifestJson.Write(Metadata('a'));
        Require(Encoding.UTF8.GetByteCount(control) < 67_108_864 && BackupManifestJson.Read(control).Artifacts.Length == 1200, "large valid metadata has a reachable bounded encoding control");
        var bounded = typeof(BackupManifestJson).Assembly.GetType("Next.Formats.ContractJson")!
            .GetMethod("WriteBounded", BindingFlags.Static | BindingFlags.NonPublic)!;
        var consumed = 0;
        IEnumerable<BackupArtifact> Counted(BackupManifest manifest)
        {
            foreach (var row in manifest.Artifacts) { consumed++; yield return row; }
        }
        string Serialize(object data, Type type)
        {
            try { return (string)bounded.MakeGenericMethod(type).Invoke(null, [BackupManifest.SchemaFamily, data])!; }
            catch (TargetInvocationException error) when (error.InnerException is JsonException json) { throw json; }
        }
        string Encode(BackupManifest manifest)
        {
            var data = new { manifest.WorldId, Artifacts = Counted(manifest) };
            return Serialize(data, data.GetType());
        }
        Require(Encode(Metadata('a')) == control && consumed == 1200, "counted rows use the same canonical envelope and artifact/image/string converters as the public manifest");
        consumed = 0;
        Reject<JsonException>(() => Encode(Metadata('雪')));
        Require(consumed < 1200, $"output budget must stop before consuming every manifest row; consumed={consumed}/1200");
        Reject<JsonException>(() => BackupManifestJson.Write(Metadata('雪')));
        var longPath = Metadata('雪').Artifacts[0].Artifact.RelativePath;
        IEnumerable<string> Strings()
        {
            for (var index = 0; index < 1200; index++) { consumed++; yield return longPath; }
        }
        consumed = 0;
        Reject<JsonException>(() => Serialize(Strings(), typeof(IEnumerable<string>)));
        Require(consumed < 1200, $"bounded writer must stop even without nested converter flushes; consumed={consumed}/1200");
    }
    private sealed class FaultStore(IWorldBackupStore inner, Action? afterBlob = null, bool failManifest = false) : IWorldBackupStore
    {
        public byte[]? ReadBlob(Hash256 hash) => inner.ReadBlob(hash);
        public byte[]? ReadManifest(Hash256 hash) => inner.ReadManifest(hash);
        public void SaveBlob(Hash256 hash, ReadOnlyMemory<byte> bytes) { inner.SaveBlob(hash, bytes); afterBlob?.Invoke(); }
        public void SaveManifest(Hash256 hash, ReadOnlyMemory<byte> bytes)
        { if (failManifest) throw new IOException("Injected backup manifest failure"); inner.SaveManifest(hash, bytes); }
    }
    public static void RawImagesAreCompleteDurableAndIndependentOfParsing()
    {
        var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            var corrupt = new byte[] { 10, 255, 3, 8 }; var artifact = new ArtifactAddress(World, "broken.dat", ArtifactKind.Nbt);
            var empty = new ArtifactAddress(World, "empty.bin", ArtifactKind.Text); var absent = new ArtifactAddress(World, "new.json", ArtifactKind.Json);
            Put(root, artifact.RelativePath, corrupt); Put(root, empty.RelativePath, []);
            ArtifactPrecondition[] inputs = [new(artifact, ArtifactImage.Present(Hash256.Compute(corrupt))), new(empty, ArtifactImage.Present(Hash256.Compute([]))), new(absent, ArtifactImage.Absent)];
            var inventory = new WorldInventory(root, World); var store = new FileSystemWorldBackupStore(state); var service = new WorldBackupService(inventory, store);
            var backup = service.Create(inputs);
            Require(backup.Manifest.Artifacts.Length == 3 && backup.ManifestHash == Hash256.Compute(Encoding.UTF8.GetBytes(BackupManifestJson.Write(backup.Manifest))), "complete backup identity binds the canonical manifest and exact physical image set");
            Require(backup.ReadArtifact(artifact)!.SequenceEqual(corrupt) && backup.ReadArtifact(empty)!.Length == 0 && backup.ReadArtifact(absent) is null, "raw invalid NBT, empty present file and expectedAbsent remain distinct without parsing");
            var replay = new WorldBackupService(inventory, new FileSystemWorldBackupStore(state)).Create(inputs.Reverse());
            Require(replay.ManifestHash == backup.ManifestHash, "restart and reordered replay reuse the immutable canonical backup");
            Put(root, artifact.RelativePath, [99]);
            var loaded = new WorldBackupService(inventory, new FileSystemWorldBackupStore(state)).Load(backup.ManifestHash);
            Require(loaded.ReadArtifact(artifact)!.SequenceEqual(corrupt) && File.ReadAllBytes(Path.Combine(root, artifact.RelativePath)).SequenceEqual(new byte[] { 99 }), "loading verified backup neither reparses nor changes the current damaged source");
            File.WriteAllBytes(Path.Combine(state, "blob-" + Hash256.Compute(corrupt).Hex + ".bin"), [1, 2, 3]);
            Reject<InvalidDataException>(() => new WorldBackupService(inventory, new FileSystemWorldBackupStore(state)).Load(backup.ManifestHash));
            Reject<InvalidDataException>(() => loaded.ReadArtifact(artifact));
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void FailedOrStaleCaptureDoesNotPublishCompletion()
    {
        foreach (var mutate in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var artifact = new ArtifactAddress(World, "a.dat", ArtifactKind.Nbt); var image = ArtifactImage.Present(Hash256.Compute(new byte[] { 1, 2, 3 })); Put(root, artifact.RelativePath, [1, 2, 3]);
                var manifest = new BackupManifest(World, [new(artifact, image)]); var id = Hash256.Compute(Encoding.UTF8.GetBytes(BackupManifestJson.Write(manifest)));
                var store = new FileSystemWorldBackupStore(state);
                var faulty = new FaultStore(store, mutate ? () => Put(root, artifact.RelativePath, [9]) : null, failManifest: !mutate);
                var service = new WorldBackupService(new WorldInventory(root, World), faulty);
                if (mutate) Reject<ArtifactConflictException>(() => service.Create([new(artifact, image)]));
                else Reject<IOException>(() => service.Create([new(artifact, image)]));
                Require(store.ReadManifest(id) is null, "source race or manifest write failure cannot publish a completed backup");
                Require(File.ReadAllBytes(Path.Combine(root, artifact.RelativePath)).SequenceEqual(mutate ? new byte[] { 9 } : new byte[] { 1, 2, 3 }), "backup failures preserve current world bytes including external changes");
                Reject<IOException>(() => service.Load(id));
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void ManifestAndStoreRejectAmbiguousOrForeignClaims()
    {
        var artifact = new ArtifactAddress(World, "a.dat", ArtifactKind.Nbt); var image = ArtifactImage.Present(Hash256.Compute(new byte[] { 1 }));
        Reject<ArgumentException>(() => new BackupManifest(Guid.Empty, [new(artifact, image)]));
        Reject<ArgumentException>(() => new BackupManifest(World, []));
        Reject<ArgumentException>(() => new BackupManifest(World, [new(artifact, image), new(new(World, "A.dat", ArtifactKind.Nbt), image)]));
        Reject<ArgumentException>(() => new BackupManifest(World, [new(new(Guid.NewGuid(), "a.dat", ArtifactKind.Nbt), image)]));
        Reject<ArgumentException>(() => new BackupManifest(World, [new(new(World, new string('x', 32_768), ArtifactKind.Nbt), image)]));
        var manifest = new BackupManifest(World, [new(artifact, image)]); var json = BackupManifestJson.Write(manifest);
        Require(BackupManifestJson.Write(BackupManifestJson.Read(json)) == json, "backup manifest exchange has a deterministic canonical roundtrip");
        Reject<NotSupportedException>(() => BackupManifestJson.Read(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Reject<JsonException>(() => BackupManifestJson.Read(json.Replace("\"data\":{", "\"data\":{\"extra\":1,", StringComparison.Ordinal)));
        var root = ScratchDirectory.CreateChild(); var foreign = ScratchDirectory.CreateChild();
        try
        {
            var store = new FileSystemWorldBackupStore(root);
            Reject<ArgumentException>(() => store.SaveBlob(Hash256.Compute(new byte[] { 2 }), new byte[] { 1 }));
            Reject<ArgumentException>(() => store.SaveManifest(Hash256.Compute(new byte[] { 2 }), Encoding.UTF8.GetBytes(json)));
            var anotherWorld = Guid.NewGuid(); var foreignManifest = new BackupManifest(anotherWorld, [new(new(anotherWorld, "a.dat", ArtifactKind.Nbt), image)]);
            var bytes = Encoding.UTF8.GetBytes(BackupManifestJson.Write(foreignManifest)); var id = Hash256.Compute(bytes); store.SaveBlob(image.Hash!, new byte[] { 1 }); store.SaveManifest(id, bytes);
            Reject<ArtifactConflictException>(() => new WorldBackupService(new WorldInventory(foreign, World), store).Load(id));
            File.WriteAllText(Path.Combine(foreign, "user.txt"), "Keep"); Reject<IOException>(() => new FileSystemWorldBackupStore(foreign));
            Require(File.ReadAllText(Path.Combine(foreign, "user.txt")) == "Keep", "backup state ownership never adopts or clears a foreign directory");
        }
        finally { Directory.Delete(root, true); Directory.Delete(foreign, true); }
    }
}
