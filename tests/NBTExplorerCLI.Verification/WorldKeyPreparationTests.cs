using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldKeyPreparationTests
{
    private sealed class FailPackStage(IArtifactStorage inner) : IArtifactStorage
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
            => artifact.Kind == ArtifactKind.Archive ? throw new IOException("Injected pack stage failure") : inner.Stage(artifact, preimage, bytes);
    }
    public static void ArchivedLanguageAndPackFailureAreOnePreparedSet()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString("Hello"))]))]);
            Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            const string pack = "z-resources.zip"; const string entry = "assets/test/lang/zh_tw.json";
            var originalPack = Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"), (entry, "{}")); Put(root, pack, originalPack);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(4325)!; var outer = new ArtifactAddress(World, pack, ArtifactKind.Archive);
            var operation = new KeyConversionPatch("archived", new(slot.Address, new NbtString("Hello"), KeyConversionRule.LiteralToTranslateComponent, profile.Id, profile.Version,
                slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Archived"), new(new(World, pack, ArtifactKind.Archive, entry), outer, "zh_tw", "test.archived", "Archived")));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]); var originalLevel = File.ReadAllBytes(Path.Combine(root, "level.dat"));
            Require(plan.Outputs.Length == 2 && plan.Dependencies.Single().Required == outer, "literal-to-component conversion declares the exact outer pack dependency");
            Reject<IOException>(() => { using var rejected = new WorldPlanPreparation(inventory, new FailPackStage(new ArtifactStorage(root, World))).Prepare(snapshot, plan, true); });
            Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "pack stage failure cleans the already-staged NBT output");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var level = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == "level.dat");
                var document = CompressedNbt.ReadDocument(File.ReadAllBytes(level.CandidatePath!), NbtCompression.GZip).Document;
                Require(NbtLocator.Resolve(document.Root, slot.Address.NodePath).Value == new NbtCompound([new("translate", new NbtString("test.archived"))]), "native scalar component changes into the exact declared translate component");
                var zipOutput = prepared.Manifest.Artifacts.Single(item => item.Artifact == outer);
                using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(zipOutput.CandidatePath!)); using var stream = zip.GetEntry(entry)!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                using var json = BoundedJson.ReadDecoded(bytes.ToArray()); Require(NativeJsonStrings.Read(json.RootElement.GetProperty("test.archived")) == "Archived", "archived language key is verified before the complete prepared set is exposed");
            }
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(originalLevel) && File.ReadAllBytes(Path.Combine(root, pack)).SequenceEqual(originalPack), "pack failure/successful prepare both preserve all original bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void PackMismatchAndLanguageCollisionPreserveWorld()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions);
            var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var component = new NbtCompound([new("text", new NbtString("Hello"))]);
            var boss = new NbtCompound([new("test:event", new NbtCompound([new("Name", component)]))]);
            Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", boss))))])), NbtCompression.GZip));
            const string packPath = "resourcepacks/client/pack.mcmeta"; const string languagePath = "resourcepacks/client/assets/test/lang/zh_tw.json";
            Put(root, packPath, "{\"pack\":{\"pack_format\":34,\"description\":\"Wrong client profile\"}}"u8.ToArray()); Put(root, languagePath, "{}"u8.ToArray());
            KeyConversionPatch Operation(WorldQuerySnapshot snapshot)
            {
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
                var path = new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1)); var profile = MinecraftProfiles.ForDataVersion(4325)!;
                return new("key", new(new(slot.Address.Artifact, path, slot.Role), component, KeyConversionRule.StructuredTextToTranslate, profile.Id, profile.Version,
                    slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, languagePath, ArtifactKind.Json), null, "zh_tw", "test.greeting", "你好")));
            }
            var inventory = new WorldInventory(root, World); var snapshot = WorldQueryTests.Query(root, cache);
            Reject<NotSupportedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [Operation(snapshot)]));
            Put(root, packPath, "{\"pack\":{\"pack_format\":55,\"description\":\"Compatible\"}}"u8.ToArray());
            Put(root, languagePath, "{\"test.greeting\":\"Original user value\"}"u8.ToArray());
            snapshot = WorldQueryTests.Query(root, cache); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [Operation(snapshot)]);
            var level = File.ReadAllBytes(Path.Combine(root, "level.dat")); var language = File.ReadAllBytes(Path.Combine(root, languagePath));
            Reject<EditRejectedException>(() => { using var rejected = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true); });
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(level) && File.ReadAllBytes(Path.Combine(root, languagePath)).SequenceEqual(language)
                && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "conflicting language dependency cleans an earlier NBT candidate without overwriting either original");
            Put(root, languagePath, Encoding.UTF8.GetBytes("{ \"test.greeting\" : \"你好\" }"));
            Put(root, packPath, "{\"pack\":{\"pack_format\":34,\"supported_formats\":[34,55],\"description\":\"Declared compatible range\"}}"u8.ToArray());
            snapshot = WorldQueryTests.Query(root, cache); plan = new WorldPlanBuilder(inventory).Build(snapshot, [Operation(snapshot)]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var output = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == languagePath);
                Require(File.ReadAllBytes(output.CandidatePath!).SequenceEqual(File.ReadAllBytes(Path.Combine(root, languagePath))), "declared pack range and same existing key/value form a valid dependency, preserving original spelling");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void IdentifyingKeyConversionRequiresReferenceAuthority()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"u8.ToArray());
            const string languagePath = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, languagePath, "{}"u8.ToArray());
            var component = new NbtCompound([new("text", new NbtString("Guard"))]);
            var entity = new NbtCompound([new("id", new NbtString("armor_stand")), new("CustomName", component)]);
            var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(4325)), new("Entities", new NbtList(NbtType.Compound, [entity]))]));
            const string path = "entities/r.0.0.mca"; var original = RegionFile.Read(new byte[8192]).WithChunk(0, document, NbtCompression.Zlib, 1); Put(root, path, original);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Guard").ToSlot("en_us", "zh_tw");
            Require(slot.Role == "armor_stand.name", "real native NBT identifying component supplies a convertible text slot");
            var componentPath = new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1)); var profile = MinecraftProfiles.ForDataVersion(4325)!;
            var conversion = new TranslationKeyConversion(new(slot.Address.Artifact, componentPath, slot.Role, slot.Address.Dimension, slot.Address.Chunk), component, KeyConversionRule.StructuredTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Guard"), new(new(World, languagePath, ArtifactKind.Json), null, "zh_tw", "name.guard", "Guard"));
            Reject<EditRejectedException>(() => new WorldPlanBuilder(new WorldInventory(root, World)).Build(snapshot, [new KeyConversionPatch("name-key", conversion)]));
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "unchanged reply text cannot authorize a structural identifier conversion");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void ModernComponentAndLanguageDependencyPrepareTogether()
    {
        foreach (var (reply, languageValue) in new[] { ("你好", "你好"), ("100%% | %s | %1$.2f | %q", "100%1$s%1$s | %1$ss | %1$s1$.2f | %1$sq") })
        {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions);
            var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var eventValue = new NbtCompound([new("action", new NbtString("run_command")), new("command", new NbtString("say Hello"))]);
            var component = new NbtCompound([new("text", new NbtString("Hello")), new("color", new NbtString("red")), new("click_event", eventValue)]);
            var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", component)]))]);
            Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"u8.ToArray());
            const string languagePath = "resourcepacks/client/assets/test/lang/zh_tw.json";
            const string languageJson = "{ \"existing\" : \"Keep\" }";
            Put(root, languagePath, Encoding.UTF8.GetBytes(languageJson));
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var path = new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1));
            var profile = MinecraftProfiles.ForDataVersion(4325)!;
            var conversion = new TranslationKeyConversion(new(slot.Address.Artifact, path, slot.Role), component, KeyConversionRule.StructuredTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", reply), new(new(World, languagePath, ArtifactKind.Json), null, "zh_tw", "test.greeting", reply));
            var inventory = new WorldInventory(root, World); var originalLevel = File.ReadAllBytes(Path.Combine(root, "level.dat"));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("key-conversion", conversion)]);
            Require(plan.Outputs.Length == 2 && plan.Dependencies.Length == 1, "key conversion declares both component and language dependency before any stage");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var level = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == "level.dat");
                var actual = CompressedNbt.ReadDocument(File.ReadAllBytes(level.CandidatePath!), NbtCompression.GZip).Document;
                var expected = new NbtCompound([new("translate", new NbtString("test.greeting")), new("color", new NbtString("red")), new("click_event", eventValue)]);
                if (reply != "你好") expected = new(expected.Entries.Append(new("with", new NbtList(NbtType.String, [new NbtString("%")]))));
                Require(NbtLocator.Resolve(actual.Root, path).Value == expected, "key profile keeps percent-looking text literal through a fixed percent argument while preserving style/event");
                var language = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == languagePath);
                using var json = BoundedJson.ReadDecoded(File.ReadAllBytes(language.CandidatePath!));
                Require(NativeJsonStrings.Read(json.RootElement.GetProperty("test.greeting")) == languageValue && NativeJsonStrings.Read(json.RootElement.GetProperty("existing")) == "Keep", "language candidate encodes the literal reply for the actual formatter and preserves existing entries");
            }
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(originalLevel) && File.ReadAllText(Path.Combine(root, languagePath)) == languageJson, "dependency preparation never commits half of a conversion");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
}
