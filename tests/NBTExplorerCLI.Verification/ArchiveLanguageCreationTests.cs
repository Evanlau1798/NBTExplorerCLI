using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class ArchiveLanguageCreationTests
{
    public static void PackDescriptionAndLanguageShareOneOutput()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string packPath = "resources.zip"; const string language = "assets/test/lang/zh_tw.json";
            var original = Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}"), (language, "{}")); Put(root, packPath, original);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == packPath && item.Text == "Resources").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(3953)!; var outer = new ArtifactAddress(World, packPath, ArtifactKind.Archive);
            var conversion = new TranslationKeyConversion(slot.Address, new NbtString("\"Resources\""), KeyConversionRule.EncodedTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "資源"), new(new(World, packPath, ArtifactKind.Archive, language), outer, "zh_tw", "test.description", "資源"));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("same-pack", conversion)]);
            Require(plan.Outputs.Length == 1 && plan.Dependencies.IsEmpty, "component source and language within the same ZIP declare one physical unit without a self-dependency");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!));
                using var metadata = new MemoryStream(); using (var stream = zip.GetEntry("pack.mcmeta")!.Open()) stream.CopyTo(metadata);
                using var json = BoundedJson.ReadDecoded(metadata.ToArray());
                Require(NativeJsonStrings.Read(json.RootElement.GetProperty("pack").GetProperty("description").GetProperty("translate")) == "test.description", "same-pack metadata component is converted");
                using var values = new MemoryStream(); using (var stream = zip.GetEntry(language)!.Open()) stream.CopyTo(values);
                using var languageJson = BoundedJson.ReadDecoded(values.ToArray());
                Require(NativeJsonStrings.Read(languageJson.RootElement.GetProperty("test.description")) == "資源", "language dependency joins the same single candidate");
            }
            Require(File.ReadAllBytes(Path.Combine(root, packPath)).SequenceEqual(original), "same-pack preparation preserves original ZIP bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void MissingEntryUsesTheExistingOuterPreimage()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string component = "{\"text\":\"Hello\"}"; WorldQueryTests.BossWorld(root, componentJson: component);
            const string metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}";
            const string packPath = "z-resources.zip"; const string language = "assets/test/lang/zh_tw.json";
            var original = Zip(("pack.mcmeta", metadata), ("assets/test/lang/en_us.json", "{\"keep\":\"Original\"}"), ("opaque.bin", "Keep")); Put(root, packPath, original);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(3953)!; var outer = new ArtifactAddress(World, packPath, ArtifactKind.Archive);
            var conversion = new TranslationKeyConversion(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root), new NbtString(component), KeyConversionRule.EncodedTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, packPath, ArtifactKind.Archive, language), outer, "zh_tw", "test.added", "你好"));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("missing-entry", conversion)]);
            Require(plan.Preconditions.Single(item => item.Artifact == outer).Image.Hash == Hash256.Compute(original), "missing entry binds the existing physical ZIP raw preimage");
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var bytes = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact == outer).CandidatePath!);
                var end = original.Length - 22; var central = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 16));
                Require(bytes.AsSpan(0, checked((int)central)).SequenceEqual(original.AsSpan(0, checked((int)central))), "adding a language entry preserves every original local record and compressed payload byte");
                using var zip = ZipArchiveValidation.OpenRead(bytes); using var stream = zip.GetEntry(language)!.Open(); using var jsonBytes = new MemoryStream(); stream.CopyTo(jsonBytes);
                using var json = BoundedJson.ReadDecoded(jsonBytes.ToArray());
                Require(NativeJsonStrings.Read(json.RootElement.GetProperty("test.added")) == "你好" && zip.Entries.Count == 4, "new language joins the existing ZIP as the only added entry");
            }
            Require(File.ReadAllBytes(Path.Combine(root, packPath)).SequenceEqual(original), "preparation never publishes the added ZIP entry");
            var external = Zip(("pack.mcmeta", metadata), ("assets/test/lang/en_us.json", "{}"), (language, "{\"external\":\"Keep\"}")); Put(root, packPath, external);
            Reject<ArtifactConflictException>(() => { using var rejected = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true); });
            Require(File.ReadAllBytes(Path.Combine(root, packPath)).SequenceEqual(external), "new entry cannot overwrite an outer ZIP changed after preview");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
