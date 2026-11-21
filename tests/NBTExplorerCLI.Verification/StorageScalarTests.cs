using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageScalarTests
{
    private const string Folder = "datapacks/p/data/demo/functions/";
    private const string Producer = Folder + "producer.mcfunction";
    private const string Consumer = Folder + "consumer.mcfunction";

    internal static void GlyphControlLists()
    {
        const string metadata = """{"pack":{"pack_format":15,"description":""}}""";
        const string font = """{"providers":[{"type":"space","advances":{"+":1,"-":1,"0":1,"1":1,"2":1,"3":1,"4":1,"5":1,"6":1,"7":1,"8":1,"9":1,"a":1,"∑":1}}]}""";
        const string numbers = """
            data modify storage demo:source staging set value []
            execute store result storage demo:source scratch int 1 run scoreboard players get #count proof_counter
            data modify storage demo:source scratch set string storage demo:source scratch
            data modify storage demo:source staging append string storage demo:source scratch 0 1
            data modify storage demo:source glyphs set from storage demo:source staging
            data modify storage demo:source glyphs append value '+'
            """;
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        var failures = new List<string>();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", font)));
            Put(root, Consumer, """
                data modify storage demo:source panel set value ['{"text":"Bienvenue"}','{"nbt":"glyphs","storage":"demo:source","interpret":true,"font":"demo:meter"}']
                tellraw @a {"nbt":"panel[]","storage":"demo:source","interpret":true}
                """u8.ToArray());
            Check("mapped constants", "data modify storage demo:source glyphs set value []\ndata modify storage demo:source glyphs append value 'a'", true);
            Check("mapped symbol", "data modify storage demo:source glyphs set value []\ndata modify storage demo:source glyphs append value '∑'", true);
            Check("numeric slice and transfer", numbers, true);
            Check("cross-storage glyph copy", """
                data modify storage demo:other staging set value []
                data modify storage demo:other staging append value 'a'
                data modify storage demo:source glyphs set from storage demo:other staging
                """, false);
            Check("unknown origin", numbers.Replace("scratch 0 1", "unknown 0 1", StringComparison.Ordinal), false);
            Check("raw mutation", numbers + "\ndata modify storage demo:source scratch set value 'not numeric'", false);
            Check("unmapped glyph", numbers + "\ndata modify storage demo:source glyphs append value 'B'", false);
            Check("mixed text", numbers + "\ndata modify storage demo:source glyphs append value '{\"text\":\"ordinary text\"}'", false);
            Check("list overwritten by its serialized string", numbers + "\ndata modify storage demo:source glyphs set string storage demo:source glyphs", false);
            var bitmap = """{"providers":[{"type":"bitmap","file":"demo:font/meter.png","ascent":1,"chars":["A\u0000"]}]}"""u8.ToArray();
            var glyphs = PackTextExtractor.ReadStorageFontGlyphs(bitmap, DecodeLimits.Default, _ => true);
            if (glyphs is null || !glyphs.SetEquals([new Rune('A')])) failures.Add("bitmap padding");
            if (PackTextExtractor.ReadStorageFontGlyphs(bitmap, DecodeLimits.Default, _ => false) is not { Count: 0 }) failures.Add("absent bitmap asset");
            Require(failures.Count == 0, "only unchanged glyph controls with verified inputs may feed translated text: " + string.Join(", ", failures));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        void Check(string name, string commands, bool expected)
        {
            Put(root, Producer, Encoding.UTF8.GetBytes(commands));
            var snapshot = WorldQueryTests.Query(root, cache);
            var visible = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Consumer).Text!.Occurrences;
            if (snapshot.Manifest.CoverageComplete != expected || expected && visible is not [{ Text: "Bienvenue" }])
                failures.Add(name);
            if (name is "cross-storage glyph copy" or "mixed text") Draft(Consumer, 1, "Bienvenue");
            if (name == "mixed text") Draft(Producer, commands.Split('\n').Length, "ordinary text");
        }

        void Draft(string path, int line, string expected)
        {
            using var result = CliScanTests.Success(CliScanTests.Run(root, "translate", "fallback", root,
                "--path", path, "--line", line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--source-language", "fr-fr", "--target-language", "de-de"));
            var data = result.RootElement.GetProperty("data");
            Require(data.GetProperty("unverified").GetArrayLength() == 0
                && data.GetProperty("candidates").EnumerateArray().Single().GetProperty("text").GetString() == expected,
                "unproven glyph operations must leave the exact prose accessible through fallback");
        }
    }

    internal static void MappedFontSlices()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string metadata = """{"pack":{"pack_format":15,"description":"Mapped glyph fixture"}}""";
        const string font = """{"providers":[{"type":"space","advances":{"A":-3,"B":2}}]}""";
        const string first = """[{"text":"A","font":"demo:meter"},{"text":"B"},{"text":"Welcome","font":"minecraft:default"}]""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", font)));
            Put(root, Consumer, """tellraw @a {"nbt":"payload","storage":"demo:source","interpret":true}"""u8.ToArray());
            Check(first, "Welcome");
            Check("""[{"text":"Start"},{"text":"A","font":"demo:meter"},{"text":"Continue"}]""", "Start", "Continue");
            Check(first.Replace("\"B\"", "\"Unmapped\"", StringComparison.Ordinal));
            Check(first.Replace("demo:meter", "demo:absent", StringComparison.Ordinal));
            Check(first, "Welcome");
            const string listReader = """tellraw @a {"nbt":"payload[]","storage":"demo:source","interpret":true}""";
            Put(root, Consumer, Encoding.UTF8.GetBytes(listReader));
            foreach (var operation in new[] { "append", "prepend", "insert 0" })
            {
                CheckWrite($"{operation} value '{first}'", true, "Welcome");
                CheckWrite($"{operation} value '{{\"text\":\"A\",\"font\":\"demo:meter\"}}'", true);
            }
            CheckWrite("set value ['" + first + "','{\"text\":\"A\"}']", true, "Welcome", "A");
            CheckWrite("set value ['" + first.Replace("demo:meter", "demo:absent", StringComparison.Ordinal) + "']", false);
            Put(root, Consumer, """tellraw @a {"nbt":"payload","storage":"demo:source","interpret":true}"""u8.ToArray());
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", font.Replace("\"B\"", "\"C\"", StringComparison.Ordinal))));
            Check(first);
            const string missingTexture = """{"providers":[{"type":"bitmap","file":"demo:font/absent.png","ascent":1,"chars":["AB"]}]}""";
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", missingTexture)));
            Check(first);
            const string mixedProviders = """{"providers":[{"type":"space","advances":{"A":-3}},{"type":"bitmap","file":"demo:font/absent.png","ascent":1,"chars":["B"]}]}""";
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", mixedProviders)));
            const string presentGlyph = """[{"text":"Welcome"},{"text":"A","font":"demo:meter"}]""";
            Check(presentGlyph, "Welcome");
            Check(presentGlyph.Replace("\"A\"", "\"B\"", StringComparison.Ordinal));
            File.Delete(Path.Combine(root, "resources.zip"));
            Put(root, "world-resources/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
            Put(root, "world-resources/assets/demo/font/meter.json", Encoding.UTF8.GetBytes(missingTexture));
            Check(first);
            Put(root, "world-resources/assets/demo/textures/font/absent.png", [1]);
            Check(first, "Welcome");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        void Check(string component, params string[] expected)
            => CheckWrite($"set value '{component}'", expected.Length != 0, expected);

        void CheckWrite(string write, bool complete, params string[] expected)
        {
            Put(root, Producer, Encoding.UTF8.GetBytes($"data modify storage demo:source payload {write}"));
            var snapshot = WorldQueryTests.Query(root, cache);
            var producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer);
            Require(snapshot.Manifest.CoverageComplete == complete
                && (producer.Text?.Occurrences.Select(slot => slot.Text) ?? []).SequenceEqual(expected),
                $"only ordinary text beside fully mapped glyphs can be translated: {write}");
        }
    }

    internal static void SavedMappedFontSlices()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string seed = "data/command_storage_demo.dat";
        const string metadata = """{"pack":{"pack_format":15,"description":"Saved glyph fixture"}}""";
        const string font = """{"providers":[{"type":"space","advances":{"A":-3,"B":2}}]}""";
        const string first = """[{"text":"A","font":"demo:meter"},"B",{"text":"Saved text","font":"minecraft:default"}]""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
            Put(root, Consumer, """tellraw @a {"nbt":"payload[]","storage":"demo:source","interpret":true}"""u8.ToArray());
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", font)));
            var items = new NbtList(NbtType.String, [new NbtString(first), new NbtString("""{"text":"A"}""")]);
            var storage = new NbtCompound([new("source", new NbtCompound([new("payload", items)]))]);
            var data = new NbtCompound([new("contents", storage)]);
            var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3465)), new("data", data)]));
            Put(root, seed, CompressedNbt.WriteDocument(document, NbtCompression.GZip));
            Check(true, "Saved text", "A");
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", font.Replace("\"B\"", "\"C\"", StringComparison.Ordinal))));
            Check(false);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        void Check(bool complete, params string[] expected)
        {
            var snapshot = WorldQueryTests.Query(root, cache);
            var value = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == seed);
            Require(snapshot.Manifest.CoverageComplete == complete && value.Text!.Occurrences.Select(slot => slot.Text).SequenceEqual(expected),
                "saved storage must preserve inherited mapped glyphs and require proof for every protected glyph");
        }
    }

    internal static void SavedNativeFontGuard()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string seed = "data/command_storage_demo.dat";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(4308, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":64,"description":"Saved native font fixture"}}"""u8.ToArray());
            Put(root, "datapacks/p/data/demo/function/reader.mcfunction", """tellraw @a {nbt:"payload[]",storage:"demo:source",interpret:true}"""u8.ToArray());
            Check("minecraft:default", true);
            Check("demo:unproven", false);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        void Check(string font, bool authorized)
        {
            var items = new NbtList(NbtType.Compound, [new NbtCompound([new("text", new NbtString("A")), new("font", new NbtString(font))])]);
            var storage = new NbtCompound([new("source", new NbtCompound([new("payload", items)]))]);
            var data = new NbtCompound([new("contents", storage)]);
            var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(4308)), new("data", data)]));
            Put(root, seed, CompressedNbt.WriteDocument(document, NbtCompression.GZip));
            var snapshot = WorldQueryTests.Query(root, cache);
            var value = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == seed);
            Require(snapshot.Manifest.CoverageComplete == authorized && (value.Text!.Occurrences is [{ Text: "A" }]) == authorized,
                $"saved native glyphs without a proven font must stay outside translation slots: {font}");
        }
    }

    internal static void ConsumerFont(bool defaultOverride = false)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        const string metadata = """{"pack":{"pack_format":15,"description":"Glyph fixture"}}""";
        const string reader = """{"nbt":"payload","storage":"demo:source","interpret":true}""";
        const string glyph = """{"text":"A","font":"demo:meter"}""";
        const string provider = """{"providers":[{"type":"space","advances":{"A":-3,"B":2}}]}""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
            Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/demo/font/meter.json", provider)));
            Put(root, Producer, """data modify storage demo:source payload set value '{"text":"A"}'"""u8.ToArray());
            Check(reader, authorized: true);
            if (defaultOverride)
            {
                var privateOnly = """{"providers":[{"type":"space","advances":{"\ue000":-3}},{"type":"bitmap","file":"demo:font/glyph.png","ascent":1,"chars":["\ue001"]}]}""";
                Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/minecraft/font/default.json", privateOnly)));
                Check(reader, authorized: true);
                Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), ("assets/minecraft/font/default.json", provider)));
                Check(reader, authorized: false);
                return;
            }
            Check(reader[..^1] + """, "font":"demo:meter"}""", authorized: false);
            Check("""{"text":"","font":"demo:meter","extra":[""" + reader + "]}", authorized: false);
            Check("[" + glyph + "," + reader + "]", authorized: false);
            Check("""{"text":"","extra":[""" + glyph + "," + reader + "]}", authorized: true);
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        void Check(string component, bool authorized)
        {
            Put(root, Consumer, Encoding.UTF8.GetBytes("tellraw @a " + component));
            var snapshot = WorldQueryTests.Query(root, cache);
            var producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer);
            Require(snapshot.Manifest.CoverageComplete == authorized
                && (producer.Text?.Occurrences is [{ Text: "A" }]) == authorized,
                $"effective consumer font must constrain the cached writer's authorization: {component}");
        }
    }

    internal static void PlanPreservesPrivateGlyphs()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Glyph fixture"}}"""u8.ToArray());
            Put(root, Producer, Encoding.UTF8.GetBytes("data modify storage demo:source payload set value '{\"text\":\"Welcome\ue000\"}'"));
            Put(root, Consumer, """tellraw @a {"nbt":"payload","storage":"demo:source","interpret":true}"""u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var text = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer).Text!.Occurrences.Single();
            var slot = text.ToSlot("en_us", "zh_tw");
            LiteralPatch Patch(string reply) => new("glyph", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", reply)));
            var builder = new WorldPlanBuilder(new WorldInventory(root, World));
            _ = builder.Build(snapshot, [Patch("歡迎\ue000")]);
            Reject<SessionReplyRejectedException>(() => builder.Build(snapshot, [Patch("歡迎")]));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    internal static void InterpretedTransfer(bool relay = false, string? unresolvedPayload = null)
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Component transfer"}}"""u8.ToArray());
            var message = unresolvedPayload ?? """{"text":"Welcome"}""";
            var set = $"data modify storage demo:source payload set value '{message}'";
            var source = relay ? "demo:relay" : "demo:source";
            var copy = relay ? "data modify storage demo:relay payload set from storage demo:source payload\n" : "";
            Put(root, Producer, Encoding.UTF8.GetBytes($"{set}\n{copy}data modify storage demo:target rows set value []\n"
                + $"data modify storage demo:target rows append from storage {source} payload\n"));
            Put(root, Consumer, """
                tellraw @a [{"nbt":"payload","storage":"demo:source","interpret":true},{"nbt":"rows[]","storage":"demo:target","interpret":true}]
                """u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer);
            if (unresolvedPayload is not null)
            {
                Require(!snapshot.Manifest.CoverageComplete && producer.Outcome == SourceOutcome.Unsupported && producer.Text!.Occurrences.IsEmpty,
                    "scalar data without proven display text stays readable and unsupported");
                return;
            }
            Require(snapshot.Manifest.CoverageComplete && producer.Outcome == SourceOutcome.Success
                && producer.Text!.Occurrences is [{ Text: "Welcome" }],
                "an exclusively interpreted scalar component remains editable through an append into an interpreted list");

            Put(root, Folder + "raw.mcfunction", "execute store result score #saved raw_length run data get storage demo:source payload"u8.ToArray());
            snapshot = WorldQueryTests.Query(root, cache);
            producer = snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == Producer);
            Require(!snapshot.Manifest.CoverageComplete && producer.Text!.Occurrences.IsEmpty,
                "an observed serialized string length revokes the interpreting producer's authority and cached text");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}
