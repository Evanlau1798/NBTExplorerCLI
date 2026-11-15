using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Text;
using System.Text.Json;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class PackMetadataTests
{
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    public static void IndependentLegacyAndMinorRanges()
    {
        var legacy = PackMetadata.Read(Bytes("{\"pack\":{\"pack_format\":71,\"description\":\"not the world version\"}}"), PackSourceKind.Datapack);
        Require(legacy.LegacyFormat == 71 && legacy.Minimum == new PackFormatVersion(71, 0) && legacy.DeclaresSupport(new(71, 999)) && !legacy.DeclaresSupport(new(99, 0)), "legacy pack version remains independent of parent world version");
        var modern = PackMetadata.Read(Bytes("{\"pack\":{\"min_format\":[99,1],\"max_format\":[99,2],\"description\":{\"text\":\"Hello\"}}}"), PackSourceKind.Datapack);
        Require(modern.LegacyFormat is null && modern.DeclaresSupport(new(99, 1)) && modern.DeclaresSupport(new(99, 2)) && !modern.DeclaresSupport(new(99, 0)) && !modern.DeclaresSupport(new(99, 3)), "minor endpoints are exact inclusive pairs");
        var major = PackMetadata.Read(Bytes("{\"pack\":{\"min_format\":[80],\"max_format\":80,\"description\":\"Hi\"}}"), PackSourceKind.ResourcePack);
        Require(major.Minimum.Minor == 0 && major.Maximum.Minor == int.MaxValue && major.DeclaresSupport(new(80, 22)), "minimum scalar/singleton is minor0, maximum scalar/singleton covers all minors");
        var range = PackMetadata.Read(Bytes("{\"pack\":{\"pack_format\":48,\"supported_formats\":[48,71],\"description\":\"Hi\"}}"), PackSourceKind.Datapack);
        Require(range.Minimum.Major == 48 && range.Maximum.Major == 71, "legacy supported_formats array is major range, distinct from modern minor pair");
    }
    public static void MalformedAndBounded()
    {
        foreach (var text in new[] {
            "{}", "{\"pack\":{\"pack_format\":48}}", "{\"pack\":{\"pack_format\":-1,\"description\":\"Hi\"}}",
            "{\"pack\":{\"min_format\":99,\"description\":\"Hi\"}}", "{\"pack\":{\"min_format\":[99,3],\"max_format\":[99,1],\"description\":\"Hi\"}}",
            "{\"pack\":{\"min_format\":99,\"max_format\":99,\"supported_formats\":99,\"description\":\"Hi\"}}",
            "{\"pack\":{\"pack_format\":48,\"pack_format\":71,\"description\":\"Hi\"}}" })
            Reject<JsonException>(() => PackMetadata.Read(Bytes(text), PackSourceKind.Datapack));
    }
    public static void JsonResourceBoundaries()
    {
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("{\"x\":1}"), new(MaxContainerEntries: 0)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("[1,2]"), new(MaxCollectionElements: 1)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("{\"x\":{}}"), new(MaxDepth: 0)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("\"abc\""), new(MaxStringBytes: 2)));
        Reject<JsonException>(() => BoundedJson.ReadDecoded([0x22, 0xff, 0x22]));
        Reject<JsonException>(() => BoundedJson.ReadDecoded(Bytes("{\"a\":1,\"\\u0061\":2}")));
        Reject<JsonException>(() => BoundedJson.ReadDecoded(Bytes("\"\\ud800\"")));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("{}"), new(MaxDecodedBytes: 1)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes("[1]"), new(MaxNodes: 1)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => BoundedJson.ReadDecoded(Bytes(new string('[', 258) + new string(']', 258))));
        using var boundary = BoundedJson.ReadDecoded(Bytes("{\"a\":[\"b\"]}"), new(MaxDecodedBytes: 11, MaxDepth: 2, MaxNodes: 3, MaxContainerEntries: 1, MaxCollectionElements: 1, MaxStringBytes: 2));
        using var exact = BoundedJson.ReadDecoded([0xef, 0xbb, 0xbf, .. Bytes("{}")], new(MaxDepth: 0, MaxNodes: 1));
        Require(exact.RootElement.ValueKind == JsonValueKind.Object, "explicit UTF8 BOM and empty root accept at exact depth/node limit");
    }
}
