using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageBookTests
{
    internal static void CheckInertPackAssets()
    {
        var root = ScratchDirectory.CreateChild();
        var cache = ScratchDirectory.CreateChild();
        const string pack = "datapacks/main/pack.mcmeta";
        const string plain = """{"pack":{"pack_format":15,"description":"Book storage"}}""";
        const string neutral = """{"pack":{"pack_format":15,"description":"Proof"}}""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/main/data/demo/functions/seed.mcfunction", """data modify storage demo:pages lines append value '{"text":"Welcome"}'"""u8.ToArray());
            Put(root, "datapacks/main/data/demo/functions/show.mcfunction", """tellraw @a {"nbt":"lines[]","storage":"demo:pages","interpret":true}"""u8.ToArray());
            Put(root, pack, Encoding.UTF8.GetBytes(plain));
            var plainDescription = Complete();
            Put(root, pack, Encoding.UTF8.GetBytes(neutral));
            ResourcePack("""{"providers":[{"type":"space","advances":{"\ue000":2}},{"type":"bitmap","file":"demo:font/test.png","ascent":1,"chars":["\ue001"]}]}""");
            var unicodeAssets = Complete();
            Require(plainDescription && unicodeAssets, $"typed pack metadata and Unicode glyph resources must preserve complete storage proof: description={plainDescription}, resources={unicodeAssets}");
            Put(root, pack, """{"pack":{"pack_format":15,"description":{"nbt":"lines[]","storage":"demo:pages","interpret":true}}}"""u8.ToArray());
            Require(!Complete(), "a dynamic pack description still blocks write authority");
            Put(root, pack, Encoding.UTF8.GetBytes(plain));
            ResourcePack("""{"providers":[{"type":"demo:opaque","payload":{"storage":"demo:pages","nbt":"lines[]"}}]}""");
            Require(!Complete(), "an unfamiliar provider is not mistaken for a known glyph asset");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        bool Complete() => WorldQueryTests.Query(root, cache).Manifest.CoverageComplete;
        void ResourcePack(string font) => Put(root, "resources.zip", Zip(("pack.mcmeta", neutral), ("assets/demo/font/default.json", font),
            ("assets/demo/lang/en_us.json", """{"demo.glyph":"\ue000"}""")));
    }

    internal static void CheckSavedPages()
    {
        var root = ScratchDirectory.CreateChild();
        var cache = ScratchDirectory.CreateChild();
        const string page = """[{"text":"Intro: "},{"nbt":"lines[]","storage":"demo:pages","interpret":true}]""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/main/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Proof"}}"""u8.ToArray());
            Put(root, "datapacks/main/data/demo/functions/seed.mcfunction", """data modify storage demo:pages lines append value '{"text":"Welcome"}'"""u8.ToArray());
            var known = Read(page);
            var edit = StorageEdit(known);
            Require(known.Manifest.CoverageComplete && edit is not null, "an interpreted native book establishes its writer's display use");
            var builder = new WorldPlanBuilder(new WorldInventory(root, World));
            _ = builder.Build(known, [edit!]);
            var literal = Read(page.Replace(":true", ":false", StringComparison.Ordinal));
            Require(StorageEdit(literal) is null, "a saved page with raw storage prevents translating its writer");
            var unknown = Read(page, """{"nbt":"lines[]","storage":"demo:pages"}""");
            if (StorageEdit(unknown) is { } preview) Reject<EditRejectedException>(() => builder.Build(unknown, [preview]));
            var opaquePage = Read(page.Replace("""{"text":"Intro: "}""", """{"text":"Intro: ","opaque":{"nbt":"lines[]","storage":"demo:pages"}}""", StringComparison.Ordinal));
            if (StorageEdit(opaquePage) is { } unsupported) Reject<EditRejectedException>(() => builder.Build(opaquePage, [unsupported]));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        WorldQuerySnapshot Read(string text, string? opaque = null)
        {
            var tag = new List<NbtEntry> { new("title", new NbtString("Guide")), new("author", new NbtString("Proof")),
                new("pages", new NbtList(NbtType.String, [new NbtString(text)])) };
            if (opaque is not null) tag.Add(new("custom", new NbtString(opaque)));
            var book = new NbtCompound([new("id", new NbtString("minecraft:written_book")), new("Count", new NbtByte(1)), new("tag", new NbtCompound(tag))]);
            var chest = new NbtCompound([new("id", new NbtString("minecraft:chest")), new("Items", new NbtList(NbtType.Compound, [book]))]);
            Put(root, "region/r.0.0.mca", Region(new("", new NbtCompound([new("DataVersion", new NbtInt(3465)),
                new("block_entities", new NbtList(NbtType.Compound, [chest]))]))));
            return WorldQueryTests.Query(root, cache);
        }

        static LiteralPatch? StorageEdit(WorldQuerySnapshot snapshot)
        {
            var literal = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).SingleOrDefault(item => item.Text == "Welcome");
            if (literal is null) return null;
            var slot = literal.ToSlot("en_us", "zh_tw");
            return new LiteralPatch("storage-text", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "歡迎")));
        }
    }

    internal static void CheckKnownPages()
    {
        var root = ScratchDirectory.CreateChild();
        var cache = ScratchDirectory.CreateChild();
        const string folder = "datapacks/main/data/demo/functions/";
        const string book = """give @s written_book{title:'Guide',author:'Proof',pages:['[{"text":"Intro: "},{"nbt":"lines[]","storage":"demo:pages","interpret":true}]']}""";
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/main/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Proof"}}"""u8.ToArray());
            Put(root, folder + "seed.mcfunction", """data modify storage demo:pages lines append value '{"text":"Welcome"}'"""u8.ToArray());
            var known = Read(book);
            Require(known.Complete && known.Welcome, "a known interpreted book page establishes complete storage display proof");
            var raw = Read(book.Replace(":true", ":false", StringComparison.Ordinal));
            Require(!raw.Complete && !raw.Welcome, "a literal book page does not authorize storage component translations");
            var opaque = Read(book[..^1] + """,custom:{item:'{"nbt":"lines[]","storage":"demo:pages","interpret":true}'}}""");
            Require(!opaque.Complete, "a known book page does not excuse unrelated opaque references");
            var extra = Read(book.Replace("""{"text":"Intro: "}""", """{"text":"Intro: ","opaque":{"nbt":"other[]","storage":"demo:opaque"}}""", StringComparison.Ordinal));
            Require(!extra.Complete, "unclassified nesting inside a known page still blocks coverage");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }

        (bool Complete, bool Welcome) Read(string source)
        {
            Put(root, folder + "book.mcfunction", Encoding.UTF8.GetBytes(source));
            var snapshot = WorldQueryTests.Query(root, cache);
            return (snapshot.Manifest.CoverageComplete, snapshot.ReadSources().Any(row => row.Text?.Occurrences.Any(item => item.Text == "Welcome") == true));
        }
    }
}
