using System.Text;
using System.Text.Json;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class EncodedKeyPreparationTests
{
    public static void JsonSnbtAndEmbeddedComponentKeepAllOtherLayers()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "datapacks/old/pack.mcmeta", "{\"pack\":{\"pack_format\":26,\"description\":\"Old\"}}"u8.ToArray());
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":22,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            const string path = "datapacks/old/data/test/loot_tables/a.json";
            const string tag = """{pages:['{"text":"Hello","extra":[{"text":"Tail"}]}','{"text":"Other"}'],protected:[I;1,2]}""";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "written_book", functions = new[] { new { function = "set_nbt", tag } } } } } } }); Put(root, path, bytes);
            var snapshot = LegacyImportMappingTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            Require(slot.Address.Syntax is { GrammarProfile: "java-snbt-legacy" } && slot.Address.ComponentPath!.ToJsonPointer() == "/text", "real loot extraction separates nativeJSON, SNBT string and embeddedJSON leaf");
            var profile = MinecraftProfiles.ForDataVersion(3700)!;
            var address = new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root, syntax: slot.Address.Syntax);
            var conversion = new TranslationKeyConversion(address, new NbtString(tag), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version,
                slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.page", "你好"));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("snbt-json", conversion)]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var output = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path);
                using var json = BoundedJson.ReadDecoded(File.ReadAllBytes(output.CandidatePath!));
                var value = NativeJsonStrings.Read(json.RootElement.GetProperty("pools")[0].GetProperty("entries")[0].GetProperty("functions")[0].GetProperty("tag"));
                const string expected = """{pages:["{\"translate\":\"test.page\",\"extra\":[{\"text\":\"Tail\"}]}",' {"text":"Other"}'],protected:[I;1,2]}""";
                Require(value == expected.Replace("' {", "'{", StringComparison.Ordinal), "only selected encoded component changes; other SNBT leaf spelling and typed array remain exact");
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(bytes), "layered key preparation preserves original JSON file");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void NativeJsonComponentPreservesPhysicalFraming()
    {
        foreach (var modern in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                if (modern) Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":71,\"description\":\"Data\"}}"u8.ToArray());
                Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes(modern ? "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}" : ResourcePack));
                const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
                const string path = "datapacks/p/data/test/advancement/a.json";
                const string component = "{\"text\":\"Hello\",\"color\":\"red\"}";
                var text = " \n{\"display\":{\"title\":" + component + "},\"n\":1e+02}\r\n";
                byte[] original = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]; Put(root, path, original);
                var snapshot = LegacyImportMappingTests.Query(root, cache);
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path).ToSlot("en_us", "zh_tw");
                var profile = MinecraftProfiles.ForDataVersion(modern ? 4325 : 3953)!; var parent = new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1));
                var conversion = new TranslationKeyConversion(new(slot.Address.Artifact, parent, slot.Role), new NbtString(component), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version,
                    slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.native", "你好"));
                var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("native-json", conversion)]);
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var output = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!);
                    var expected = text.Replace("\"text\":\"Hello\"", "\"translate\":\"test.native\"", StringComparison.Ordinal);
                    Require(output.SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(expected))), "native JSON key conversion preserves BOM/whitespace/numeric/style bytes for both source profiles");
                }
                Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "native JSON preparation preserves original physical artifact");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
    public static void LegacyJsonComponentUsesItsExactDecodedRoot()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string original = "{\"text\":\"Hello\",\"color\":\"red\",\"extra\":[{\"text\":\"Tail\"}],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"say Hello\"}}";
            LegacyImportMappingTests.BossWorld(root, componentJson: original);
            Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes(ResourcePack));
            const string languagePath = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, languagePath, "{}"u8.ToArray());
            var snapshot = LegacyImportMappingTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            Require(slot.Address.ComponentPath!.ToJsonPointer() == "/text", "actual legacy profile separates physical NBT string and decoded component leaf");
            var address = new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root); var profile = MinecraftProfiles.ForDataVersion(3953)!;
            var conversion = new TranslationKeyConversion(address, new NbtString(original), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version,
                slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, languagePath, ArtifactKind.Json), null, "zh_tw", "test.greeting", "你好"));
            var inventory = new WorldInventory(root, World); var operation = new KeyConversionPatch("encoded", conversion);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var output = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == "level.dat");
                var document = CompressedNbt.ReadDocument(File.ReadAllBytes(output.CandidatePath!), NbtCompression.GZip).Document;
                var actual = (NbtString)NbtLocator.Resolve(document.Root, slot.Address.NodePath).Value!;
                const string expected = "{\"translate\":\"test.greeting\",\"color\":\"red\",\"extra\":[{\"text\":\"Tail\"}],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"say Hello\"}}";
                Require(actual.Value == expected, "encoded profile changes only the declared component text/key and preserves sibling text, style and executable event bytes");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
