using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageBookTests
{
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
