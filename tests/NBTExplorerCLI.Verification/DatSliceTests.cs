using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class DatSliceTests
{
    private static NbtDocument Document => new("world", new NbtCompound([
        new("text", new NbtString("Hello %s")), new("number", new NbtInt(42)),
        new("UUID", new NbtIntArray([1, 2, 3, 4])), new("keep", new NbtFloat(unchecked((int)0x80000000)))]));
    public static void TwoAuthorizedEdits(string fixtureRoot)
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "codec", "dat-slice-oracle.json")));
            var original = oracle.RootElement.GetProperty("sourceBase64").GetBytesFromBase64(); var originalHash = Hash256.Compute(original);
            Require(CompressedNbt.ReadDocument(original, NbtCompression.GZip).Document == Document, "independent fixed DAT source agrees with typed model");
            foreach (var typed in new[] { false, true })
            {
                var filename = typed ? "typed.dat" : "literal.dat";
                var address = new SourceAddress(new(World, filename, ArtifactKind.Nbt), TypedPath.Root, "nbt.document");
                var path = Path.Combine(root, filename); File.WriteAllBytes(path, original);
                var image = ArtifactImage.Present(Hash256.Compute(original));
                var key = new TypedPath([PathSegment.ForKey(typed ? "number" : "text")]);
                var target = new SourceAddress(address.Artifact, key, typed ? "nbt.value" : "item.name");
                var slot = new TextSlot(target, "Hello %s", "test", "en_us", "zh_tw", false, new(["%s"]));
                PatchOperation operation = typed ? new TypedPatch("typed", target, TypedEdit.Replace(key, new NbtInt(42), new NbtLong(long.MaxValue)))
                    : new LiteralPatch("literal", new(slot, Reply(slot, "你好 %s")));
                var store = new ArtifactStorage(root, World);
                using var candidate = DatPreparation.Prepare(store, address, NbtCompression.GZip, image, [operation]);
                Require(File.ReadAllBytes(path).SequenceEqual(original), "read/locate/edit/stage/verify never implicitly changes original");
                var candidateDocument = CompressedNbt.ReadDocument(File.ReadAllBytes(candidate.CandidatePath), NbtCompression.GZip).Document;
                var candidateRaw = NbtCompressionCodec.Decode(File.ReadAllBytes(candidate.CandidatePath), NbtCompression.GZip).Bytes;
                Require(candidateRaw.SequenceEqual(oracle.RootElement.GetProperty(typed ? "typedRawBase64" : "literalRawBase64").GetBytesFromBase64()), "persisted candidate equals independent Python authorized postimage bytes");
                var expected = typed ? TypedTreeEdits.Apply(Document, [((TypedPatch)operation).Edit]) : LiteralTranslations.Apply(address, Document, [((LiteralPatch)operation).Translation]);
                Require(candidateDocument == expected && NbtLocator.Resolve(candidateDocument.Root, new([PathSegment.ForKey("UUID")])).Value == new NbtIntArray([1, 2, 3, 4]), "exact whole-tree authorization and nonselected types preserved");
                candidate.Commit();
                Require(CompressedNbt.ReadDocument(store.Read(address.Artifact), NbtCompression.GZip).Document == expected, "explicit disk replace and reparse completes vertical slice");
            }
            Require(Hash256.Compute(oracle.RootElement.GetProperty("sourceBase64").GetBytesFromBase64()) == originalHash, "fixed DAT fixture source remains unchanged");
            Require(Hash256.Compute(BinaryNbtTests.Golden(fixtureRoot)).Hex == "d05a391517fe31af307aafaf5addecbc788ab493117d25b7d153f549ac3fe977", "independent source fixture hash unchanged");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void RejectsStaleScopeAndCorrupt()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); var original = CompressedNbt.WriteDocument(Document, NbtCompression.Zlib);
            File.WriteAllBytes(path, original); var image = ArtifactImage.Present(Hash256.Compute(original));
            var store = new ArtifactStorage(root, World); var address = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt), TypedPath.Root, "nbt.document");
            var key = new TypedPath([PathSegment.ForKey("number")]); var target = new SourceAddress(address.Artifact, key, "nbt.value");
            PatchOperation Valid(int before) => new TypedPatch("op", target, TypedEdit.Replace(key, new NbtInt(before), new NbtInt(43)));
            Reject<ArtifactConflictException>(() => DatPreparation.Prepare(store, address, NbtCompression.Zlib, ArtifactImage.Absent, [Valid(42)]));
            Reject<EditRejectedException>(() => DatPreparation.Prepare(store, address, NbtCompression.Zlib, image, [Valid(41)]));
            var wrong = new SourceAddress(new(World, "other.dat", ArtifactKind.Nbt), key, "nbt.value");
            Reject<ArgumentException>(() => DatPreparation.Prepare(store, address, NbtCompression.Zlib, image, [new TypedPatch("wrong", wrong, ((TypedPatch)Valid(42)).Edit)]));
            BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => DatPreparation.Prepare(store, address, NbtCompression.Zlib, image, [Valid(42)], new(MaxDecodedBytes: 1)));
            File.WriteAllBytes(path, original[..^1]);
            BinaryNbtTests.RejectCodec(CodecError.CorruptCompression, () => DatPreparation.Prepare(store, address, NbtCompression.Zlib, ArtifactImage.Present(Hash256.Compute(original[..^1])), [Valid(42)]));
            Require(File.ReadAllBytes(path).SequenceEqual(original[..^1]) && Directory.GetFiles(root, "*.candidate").Length == 0, "rejections do not change source or leave staged output");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void NoOpPreservesTimestamp()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "level.dat"); var original = CompressedNbt.WriteDocument(Document, NbtCompression.GZip);
            File.WriteAllBytes(path, original); File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var timestamp = File.GetLastWriteTimeUtc(path); var store = new ArtifactStorage(root, World);
            var address = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt), TypedPath.Root, "nbt.document");
            var key = new TypedPath([PathSegment.ForKey("number")]); var target = new SourceAddress(address.Artifact, key, "nbt.value");
            using var candidate = DatPreparation.Prepare(store, address, NbtCompression.GZip, ArtifactImage.Present(Hash256.Compute(original)),
                [new TypedPatch("noop", target, TypedEdit.Replace(key, new NbtInt(42), new NbtInt(42)))]);
            candidate.Commit();
            Require(File.ReadAllBytes(path).SequenceEqual(original) && File.GetLastWriteTimeUtc(path) == timestamp, "no-op explicit commit must retain source bytes and timestamp");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
