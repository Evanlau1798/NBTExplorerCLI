using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.NbtItemTextTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldTextBoundaryTests
{
    public static void FunctionNewlineAndBomSpans()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            foreach (var (name, body) in new[] { ("cr", "say First\rsay Second\r"), ("crlf", "say First\r\n"), ("lf", "say First\n"), ("none", "say First"), ("mixed", "say First\r\nsay Second\n") })
            {
                var path = "datapacks/p/data/models/function/" + name + ".mcfunction"; var bytes = Encoding.UTF8.GetBytes(body); Put(root, path, bytes);
                var scan = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], ["command.function"], [path])));
                var result = scan.ReadSources().Single();
                Require(result.Outcome == SourceOutcome.Unsupported && result.DiagnosticCode == "UnknownCommandGrammar" && result.Commands!.ReferenceCompleteness == ReferenceCompleteness.Unresolved, "unknown function grammar remains explicitly incomplete");
                var address = result.Text!.Diagnostics.First().Address; var syntax = address.Syntax!;
                Require(syntax.StartByte == 0 && syntax.LengthBytes == Encoding.UTF8.GetByteCount("say First") && syntax.PreimageHash == Hash256.Compute(bytes).Hex && syntax.Newline == name
                    && SourceAddressJson.Read(SourceAddressJson.Write(address)) == address, "exact source bytes/newline and serialized syntax locator survive per-line grammar diagnostics");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    public static void FunctionBomKeepsPrefixOutsideSpan()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            const string path = "datapacks/p/data/models/function/bom.mcfunction";
            const string body = "say 雪\r\n"; var encoded = Encoding.UTF8.GetBytes(body); byte[] bytes = [0xef, 0xbb, 0xbf, .. encoded]; Put(root, path, bytes);
            var scan = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], ["command.function"], [path])));
            var diagnostic = scan.ReadSources().Single().Text!.Diagnostics.Single(); var syntax = diagnostic.Address.Syntax!;
            Require(syntax.Encoding == "utf8-bom" && syntax.StartByte == 3 && syntax.LengthBytes == Encoding.UTF8.GetByteCount("say 雪") && syntax.PreimageHash == Hash256.Compute(bytes).Hex,
                "BOM remains in raw preimage and outside command body span; it is not command text");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void DatFramingAndFrozenStoragePolicy()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false);
            foreach (var codec in new[] { NbtCompression.Raw, NbtCompression.Zlib })
            {
                var item = C(E("id", S("minecraft:paper")), E("components", C(E("minecraft:custom_name", Text(codec.ToString(), false)))));
                var data = new NbtDocument("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item))));
                Put(root, "playerdata/" + codec + ".dat", CompressedNbt.WriteDocument(data, codec));
            }
            const string storagePath = "data/command_storage_golden.dat";
            var storage = new NbtDocument("", C(E("DataVersion", new NbtInt(3953)), E("data", C(E("contents", C(E("golden:key", C(E("value", S("Literal"))))))))));
            Put(root, storagePath, CompressedNbt.WriteDocument(storage, NbtCompression.GZip));
            var key = new ArtifactAddress(World, storagePath, ArtifactKind.Nbt);
            var rules = new List<StorageTextRule> { new(new(new[] { "data", "contents", "golden:key", "value" }.Select(PathSegment.ForKey)), StorageTextKind.Literal) };
            var policy = new Dictionary<ArtifactAddress, IReadOnlyList<StorageTextRule>> { [key] = rules };
            var scan = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All), policy); rules.Clear(); policy.Clear();
            var rows = scan.ReadSources().ToArray();
            Require(rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant) && rows.SelectMany(row => row.Text?.Occurrences ?? []).Select(text => text.Text).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "Literal", "Raw", "Zlib" }.Order(StringComparer.Ordinal)), "strict raw/zlib/GZip DAT adapters share own-version extraction and immutable explicit rule policy");
            var without = new WorldTextExtraction(new WorldInventory(root, World), new(SnapshotScope.All)).ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == storagePath);
            Require(without.Outcome == SourceOutcome.Unsupported && without.Text!.Unclassified.Length == 1, "missing policy keeps unknown storage text, including default immutable-array case");
        }
        finally { Directory.Delete(root, true); }
    }
}
