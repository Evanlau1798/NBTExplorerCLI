using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NBTExplorerCLI.Verification;

internal static class SourceAddressTests
{
    private static readonly Guid World = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public static void LosslessRoundtrip()
    {
        var path = new TypedPath([PathSegment.ForKey(""), PathSegment.ForKey("/~.[0]#!你好"), PathSegment.ForIndex(0),
            PathSegment.ForKey("\ud800")]);
        var address = new SourceAddress(new(World, "packs/a.zip", ArtifactKind.Archive, "assets/test/lang/en_us.json"),
            path, "lang.value", componentPath: TypedPath.Root, jsonTarget: JsonTargetKind.PropertyName);
        var decoded = SourceAddressJson.Read(SourceAddressJson.Write(address));
        Require(address == decoded, "地址交換必須保留所有位置、空 key、分隔符與 UTF-16 code units");
        Require(!new TypedPath([PathSegment.ForKey("0")]).Equals(new TypedPath([PathSegment.ForIndex(0)])),
            "文字 key 0 與 index 0 不能混用");
        Require(new TypedPath([PathSegment.ForKey("a/~"), PathSegment.ForIndex(2)]).ToJsonPointer() == "/a~1~0/2",
            "JSON Pointer 必須遵循 RFC 6901 escaping");
    }

    public static void RejectsUnsafeAddresses()
    {
        foreach (var path in new[] { "../escape.dat", "/absolute.dat", "C:/absolute.dat", "a\\b.dat", "a//b.dat", "data/../x.dat" })
            Reject(() => new ArtifactAddress(World, path, ArtifactKind.Nbt));
        Reject(() => new ArtifactAddress(Guid.Empty, "a.dat", ArtifactKind.Nbt));
        Reject(() => new ArtifactAddress(World, "a.zip", ArtifactKind.Archive, "../escape"));
        Reject(() => PathSegment.ForIndex(-1));
        Reject(() => new SourceAddress(new(World, "region/r.-1.0.mca", ArtifactKind.Region), TypedPath.Root,
            "sign", "minecraft:overworld", new(0, 0)));
    }

    public static void NegativeChunksAndExactSources()
    {
        var chunk = new ChunkPosition(-1, -33);
        Require(chunk.RegionX == -1 && chunk.RegionZ == -2 && chunk.LocalX == 31 && chunk.LocalZ == 31,
            "負 chunk 座標必須採 floor division 與非負 modulo");
        var first = new SourceAddress(new(World, "region/r.-1.-2.mca", ArtifactKind.Region), TypedPath.Root,
            "sign", "minecraft:overworld", chunk);
        var second = new SourceAddress(new(World, "DIM-1/region/r.-1.-2.mca", ArtifactKind.Region), TypedPath.Root,
            "sign", "minecraft:the_nether", chunk);
        Require(first != second, "同名 region 的不同維度不能選到同一來源");
    }

    public static void StrictJsonBoundary()
    {
        var region = new SourceAddress(new(World, "region/r.0.0.mca", ArtifactKind.Region), TypedPath.Root,
            "sign", "minecraft:overworld", new(0, 0));
        var valid = SourceAddressJson.Write(region);
        var missingChunk = JsonNode.Parse(valid)!;
        missingChunk["chunk"]!.AsObject().Remove("x");
        var syntax = new SourceAddress(new(World, "data/test.mcfunction", ArtifactKind.McFunction), TypedPath.Root,
            "command.text", syntax: new("java-1.21", 1, 0, 10, new string('0', 64), "utf-8", "crlf"));
        var missingSpan = JsonNode.Parse(SourceAddressJson.Write(syntax))!;
        missingSpan["syntax"]!.AsObject().Remove("startByte");
        var duplicate = valid.Replace("\"semanticRole\":\"sign\"", "\"semanticRole\":\"other\",\"semanticRole\":\"sign\"", StringComparison.Ordinal);
        var accepted = new List<string>();
        foreach (var item in new[] { ("missing chunk x", missingChunk.ToJsonString()),
            ("missing startByte", missingSpan.ToJsonString()), ("duplicate member", duplicate) })
        {
            try { SourceAddressJson.Read(item.Item2); accepted.Add(item.Item1); }
            catch (JsonException) { }
        }
        Require(accepted.Count == 0, "Strict contract wrongly accepted: " + string.Join(", ", accepted));
        Require(syntax == SourceAddressJson.Read(SourceAddressJson.Write(syntax)), "完整 syntax locator 必須可逆");
    }

    public static void MalformedLosslessStrings()
    {
        var address = new SourceAddress(new(World, "level.dat", ArtifactKind.Nbt),
            new([PathSegment.ForKey("\ud800")]), "literal");
        var valid = SourceAddressJson.Write(address);
        foreach (var payload in new[] { "{\"utf16be\":\"\\uD800\"}", "{\"utf16be\":\"D8\"}",
            "{\"utf16be\":\"GGGG\"}", "{\"utf16be\":null}", "\"\\uD800\"" })
        {
            var malformed = valid.Replace("{\"utf16be\":\"D800\"}", payload, StringComparison.Ordinal);
            Require(malformed != valid, "測試必須替換到實際 lossless string");
            try { SourceAddressJson.Read(malformed); }
            catch (JsonException) { continue; }
            throw new InvalidOperationException("Malformed string 必須回 JsonException");
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("非法地址必須在純驗證邊界拒絕");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
