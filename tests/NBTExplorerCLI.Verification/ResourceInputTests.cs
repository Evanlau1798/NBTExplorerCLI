using System.Text;
using System.Collections.Immutable;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class ResourceInputTests
{
    private const string Metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep\"},\"opaque\":1}";
    private static ResourceGeneration Request() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [new("zh_tw", [new("map.test", "譯文")])], new(34, 0));
    public static void ZipAndDirectoryInputsFreezeExactBytesAndVerifyEveryStoredObject()
    {
        foreach (var zip in new[] { false, true })
        {
            var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var files = new Dictionary<string, byte[]> { ["pack.mcmeta"] = Encoding.UTF8.GetBytes(Metadata), ["assets/test/lang/en_us.json"] = "{\"keep\":\"Original \\ud800\"}"u8.ToArray(), ["assets/test/textures/opaque.bin"] = [0, 255, 13, 0] };
                foreach (var file in files) Put(source, file.Key, file.Value);
                if (zip)
                {
                    byte[] emptyZip = [0x50, 0x4b, 5, 6, .. new byte[18]];
                    Put(source, "input.zip", ZipArchiveValidation.AddEntries(emptyZip, files.Select(file => (file.Key, file.Value))));
                }
                var inventory = new WorldInventory(source, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")); var objects = new FileSystemWorldBackupStore(state);
                var service = new ResourceInputService(objects); var generation = service.Capture(Request(), inventory, zip ? new(inventory.WorldId, "input.zip", ArtifactKind.Archive) : null);
                Require(generation.WorldId == World && generation.Merge is not null && generation.Merge.Entries.Length == files.Count, "immutable source capture keeps target scope separate and declares every resource file");
                var loaded = new ResourceInputService(new FileSystemWorldBackupStore(state)).Load(generation);
                Require(files.All(file => loaded.Read(file.Key).SequenceEqual(file.Value)), "fresh service reads verified original metadata, language escapes and opaque bytes");
                var detached = loaded.Read("assets/test/textures/opaque.bin"); detached[0] = 9;
                Require(loaded.Read("assets/test/textures/opaque.bin")[0] == 0, "callers cannot mutate the immutable verified input through returned buffers");
                if (zip) Put(source, "input.zip", [9]); else foreach (var file in files) Put(source, file.Key, [9]);
                var replay = new ResourceInputService(new FileSystemWorldBackupStore(state)).Load(generation);
                Require(files.All(file => replay.Read(file.Key).SequenceEqual(file.Value)), "source changes after capture do not alter the frozen merge intent");
                File.WriteAllBytes(Path.Combine(state, "blob-" + generation.Merge!.BlobHash.Hex + ".bin"), [9]);
                var rejected = false; try { _ = service.Load(generation); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "tampered immutable source index cannot become an accepted merge input");
            }
            finally { Directory.Delete(source, true); Directory.Delete(state, true); }
        }
    }
    public static void InvalidResourceSourcesRefuseBeforePublishingReusableInputs()
    {
        foreach (var shape in new[] { "gameplay", "empty-gameplay-directory", "world-metadata", "unknown-format", "nested-language", "reserved" })
        {
            var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild(); var objectsPath = Path.Combine(state, "objects");
            try
            {
                var extra = shape switch { "gameplay" => "data/test/function/run.mcfunction", "empty-gameplay-directory" => "data/", "world-metadata" => "level.dat", "reserved" => "assets/test/CON.png", _ => "assets/test/lang/en_us.json" };
                var value = shape == "nested-language" ? "{\"bad\":{}}" : "{\"ok\":\"Text\"}";
                Put(source, "input.zip", Zip(("pack.mcmeta", shape == "unknown-format" ? Metadata.Replace("34", "999", StringComparison.Ordinal) : Metadata), (extra, value)));
                var inventory = new WorldInventory(source, World); var original = File.ReadAllBytes(Path.Combine(source, "input.zip"));
                var service = new ResourceInputService(new FileSystemWorldBackupStore(objectsPath));
                var initialObjects = Directory.GetFiles(objectsPath).Order(StringComparer.Ordinal).ToArray(); var refused = false;
                try { _ = service.Capture(Request(), inventory, new(World, "input.zip", ArtifactKind.Archive)); }
                catch (Exception failure) when (failure is ArgumentException or CodecException or System.Text.Json.JsonException or InvalidDataException or NotSupportedException) { refused = true; }
                Require(refused && initialObjects.SequenceEqual(Directory.GetFiles(objectsPath).Order(StringComparer.Ordinal)) && File.ReadAllBytes(Path.Combine(source, "input.zip")).SequenceEqual(original), "invalid resource source must preserve original bytes and publish no reusable input: " + shape);
            }
            finally { Directory.Delete(source, true); Directory.Delete(state, true); }
        }
    }
    public static void ChangedSourceCatalogContentOrRootCannotPublishAnAcceptedIndex()
    {
        foreach (var change in new[] { "content", "catalog", "binding" })
        {
            var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Put(source, "pack.mcmeta", Encoding.UTF8.GetBytes(Metadata)); Put(source, "assets/test/lang/en_us.json", "{\"keep\":\"Original\"}"u8.ToArray());
                var inventory = new ChangingInventory(new WorldInventory(source, World), source, change);
                var refused = false; try { _ = new ResourceInputService(new FileSystemWorldBackupStore(state)).Capture(Request(), inventory); }
                catch (ArtifactConflictException) { refused = true; }
                var manifests = Directory.GetFiles(state, "manifest-*.json");
                Require(refused && manifests.All(path => !File.Exists(Path.Combine(state, "blob-" + Path.GetFileNameWithoutExtension(path)[9..] + ".bin"))), "changed source must not publish the immutable resource input index: " + change);
            }
            finally { Directory.Delete(source, true); Directory.Delete(state, true); }
        }
    }
    public static void EveryRawBlobAndDeclaredEntryAreVerifiedOnLoad()
    {
        foreach (var change in new[] { "raw", "missing", "declaration" })
        {
            var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                Put(source, "pack.mcmeta", Encoding.UTF8.GetBytes(Metadata)); Put(source, "assets/test/opaque.bin", [0, 255]);
                var objects = new FileSystemWorldBackupStore(state); var service = new ResourceInputService(objects);
                var generation = service.Capture(Request(), new WorldInventory(source, World)); var entry = generation.Merge!.Entries.Single(item => item.Path.EndsWith("opaque.bin", StringComparison.Ordinal));
                var raw = Path.Combine(state, "blob-" + entry.Hash.Hex + ".bin");
                if (change == "raw") File.WriteAllBytes(raw, [9]);
                else if (change == "missing") File.Delete(raw);
                else generation = new(World, generation.Format, generation.Destination, generation.Languages, generation.PackFormat,
                    generation.Merge with { Entries = generation.Merge.Entries.Select(item => item == entry ? item with { Bytes = 3 } : item).ToImmutableArray() });
                var refused = false; try { _ = service.Load(generation); } catch (Exception failure) when (failure is InvalidDataException or FileNotFoundException) { refused = true; }
                Require(refused, "resource load verifies the complete index, every raw blob and declared lengths: " + change);
            }
            finally { Directory.Delete(source, true); Directory.Delete(state, true); }
        }
    }
    private sealed class ChangingInventory(WorldInventory inner, string root, string change) : IWorldInventory
    {
        private int reads, catalogs;
        public Guid WorldId => inner.WorldId;
        public Hash256? AccessBinding => change == "binding" && reads > 2 ? Hash256.Compute([9]) : inner.AccessBinding;
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000)
        {
            if (++catalogs == 2 && change == "catalog") Put(root, "assets/test/added.bin", [9]);
            return inner.Enumerate(maxEntries);
        }
        public byte[] Read(ArtifactAddress artifact)
        {
            var bytes = inner.Read(artifact);
            if (++reads == 2 && change == "content") Put(root, "assets/test/lang/en_us.json", "{\"keep\":\"Changed\"}"u8.ToArray());
            return bytes;
        }
    }
}
