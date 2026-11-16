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
    public static void EncodedKeysAndLiteralSiblingsUseOnePhysicalRewrite()
    {
        foreach (var mode in new[] { "nbt", "json", "snbt" })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                const string component = "[{\"text\":\"Hello\",\"color\":\"red\"},\"Tail\",{\"text\":\"Other\"}]";
                var tag = "{pages:['" + component + "'],Protected:7}"; string path;
                if (mode == "nbt") { path = "level.dat"; LegacyImportMappingTests.BossWorld(root, componentJson: component); }
                else if (mode == "json") { path = "datapacks/p/data/test/advancement/array.json"; Put(root, path, Encoding.UTF8.GetBytes("{\"display\":{\"title\":" + component + "}}")); }
                else
                {
                    path = "datapacks/p/data/test/loot_tables/array.json"; Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":26,\"description\":\"Old\"}}"u8.ToArray());
                    Put(root, path, JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "written_book", functions = new[] { new { function = "set_nbt", tag } } } } } } }));
                }
                Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":" + (mode == "snbt" ? 22 : 34) + ",\"description\":\"Resources\"}}"));
                const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
                var snapshot = LegacyImportMappingTests.Query(root, cache); var profile = MinecraftProfiles.ForDataVersion(mode == "snbt" ? 3700 : 3953)!;
                var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.Artifact.RelativePath == path).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
                Require(slots.Length == 3, "real source produces three disjoint component leaves before mixed conversion");
                var operations = new List<PatchOperation>();
                foreach (var (text, key) in new[] { ("Hello", "test.one"), ("Other", "test.two") })
                {
                    var slot = slots.Single(item => item.SourceText == text);
                    var node = mode == "json" ? new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1)) : slot.Address.NodePath;
                    var decoded = mode == "json" ? null : new TypedPath(slot.Address.ComponentPath!.Segments.Take(slot.Address.ComponentPath.Segments.Length - 1));
                    var address = new SourceAddress(slot.Address.Artifact, node, slot.Role, componentPath: decoded, syntax: slot.Address.Syntax);
                    var expected = mode == "snbt" ? tag : mode == "nbt" ? component : text == "Hello" ? "{\"text\":\"Hello\",\"color\":\"red\"}" : "{\"text\":\"Other\"}";
                    operations.Add(new KeyConversionPatch(key, new(address, new NbtString(expected), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot,
                        new(slot.Id, slot.SourceTextHash, "zh_tw", "譯:" + text), new(new(World, language, ArtifactKind.Json), null, "zh_tw", key, "譯:" + text))));
                }
                var tail = slots.Single(item => item.SourceText == "Tail"); operations.Add(new LiteralPatch("tail", new(tail, new(tail.Id, tail.SourceTextHash, "zh_tw", "尾"))));
                var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations); var original = File.ReadAllBytes(Path.Combine(root, path));
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var bytes = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!); string jsonText;
                    if (mode == "nbt") jsonText = ((NbtString)NbtLocator.Resolve(CompressedNbt.ReadDocument(bytes, NbtCompression.GZip).Document.Root, slots[0].Address.NodePath).Value!).Value;
                    else
                    {
                        using var json = BoundedJson.ReadDecoded(bytes);
                        if (mode == "json") jsonText = json.RootElement.GetProperty("display").GetProperty("title").GetRawText();
                        else
                        {
                            var snbt = NativeJsonStrings.Read(json.RootElement.GetProperty("pools")[0].GetProperty("entries")[0].GetProperty("functions")[0].GetProperty("tag"));
                            jsonText = ((NbtString)((LogicalMinecraftLeaf)Snbt.ParseDocument(Encoding.UTF8.GetBytes(snbt), SnbtProfile.JavaLegacy).Resolve(new([PathSegment.ForKey("pages"), PathSegment.ForIndex(0)])).Value).Value).Value;
                        }
                    }
                    using var componentJson = BoundedJson.ReadDecoded(Encoding.UTF8.GetBytes(jsonText)); var array = componentJson.RootElement;
                    Require(NativeJsonStrings.Read(array[0].GetProperty("translate")) == "test.one" && NativeJsonStrings.Read(array[0].GetProperty("color")) == "red"
                        && NativeJsonStrings.Read(array[1]) == "尾" && NativeJsonStrings.Read(array[2].GetProperty("translate")) == "test.two", "two key roots and literal sibling compose without stale binding or duplicate physical rewrites");
                }
                Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "mixed encoded preparation preserves original artifact bytes");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
    public static void NativeSnbtComponentPreservesItsOuterLayers()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":71,\"description\":\"Modern\"}}"u8.ToArray());
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            const string path = "datapacks/p/data/test/item_modifier/a.json";
            const string snbt = "{id:'text_display',text:{text:'Hello',color:'red'},Age:7}";
            var original = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["minecraft:entity_data"] = snbt } }); Put(root, path, original);
            var snapshot = LegacyImportMappingTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var parsed = Snbt.ParseDocument(Encoding.UTF8.GetBytes(snbt), SnbtProfile.Java1215); var node = new TypedPath([PathSegment.ForKey("text")]); var span = parsed.Resolve(node).Span; var syntax = slot.Address.Syntax!;
            Require(slot.Address.ComponentPath is null && syntax.GrammarProfile == "java-snbt-1.21.5" && syntax.PreimageHash == parsed.SourceHash.Hex, "real modern entity_data extraction retains native SNBT component and leaf layers");
            var address = new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, syntax: new(syntax.GrammarProfile, syntax.GrammarVersion, span.StartByte, span.LengthBytes, syntax.PreimageHash, syntax.Encoding, syntax.Newline, node));
            var profile = MinecraftProfiles.ForDataVersion(4325)!;
            var operation = new KeyConversionPatch("native-snbt", new(address, new NbtString(snbt), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version,
                slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.native_snbt", "你好")));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                using var json = BoundedJson.ReadDecoded(File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!));
                var actual = NativeJsonStrings.Read(json.RootElement.GetProperty("components").GetProperty("minecraft:entity_data"));
                Require(actual.StartsWith("{id:'text_display',text:", StringComparison.Ordinal) && actual.EndsWith(",Age:7}", StringComparison.Ordinal), "native SNBT conversion preserves all raw bytes outside its selected component");
                var document = MinecraftNbtAdapter.Encode(new("", Snbt.ParseDocument(Encoding.UTF8.GetBytes(actual), SnbtProfile.Java1215).Root.Value), profile);
                Require(NbtLocator.Resolve(document.Root, node).Value == new NbtCompound([new("translate", new NbtString("test.native_snbt")), new("color", new NbtString("red"))]), "selected native component keeps exact style values and only changes its text/key field");
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "native SNBT key preparation never publishes the JSON artifact");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
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
