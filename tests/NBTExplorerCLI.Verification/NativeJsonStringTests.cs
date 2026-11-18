using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class NativeJsonStringTests
{
    public static void ExactJavaCodeUnits()
    {
        using var native = JsonDocument.Parse("{\"\\ud800\":\"\\udfff\",\"text\":\"\\ud800\",\"escapes\":\"\\\\\\\"\\b\\f\\n\\r\\t\\/\\u0000\\uD83D\\uDE00\"}");
        var properties = native.RootElement.EnumerateObject().ToArray();
        Require(NativeJsonStrings.PropertyName(properties[0]) == "\ud800" && NativeJsonStrings.Read(properties[0].Value) == "\udfff", "exact Gson 2.11 independent vector: d800=dfff");
        Require(NativeJsonStrings.Read(properties[1].Value) == "\ud800" && NativeJsonStrings.Read(properties[2].Value) == "\\\"\b\f\n\r\t/\0\ud83d\ude00", "all JSON escapes and paired/unpaired UTF16 remain exact, without replacement");
        Require(NativeJsonStrings.PropertyName(properties[1]) == "text", "ordinary names use the same API");
        Reject<JsonException>(() => NativeJsonStrings.Read(native.RootElement));
    }
    public static void BoundedReaderPreservesNativeUnicode()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"\\ud800\":\"\\udfff\",\"literal\":\"雪😀\"}");
        using var native = BoundedJson.ReadDecoded(bytes);
        var properties = native.RootElement.EnumerateObject().ToArray();
        var name = NativeJsonStrings.PropertyName(properties[0]);
        var address = new SourceAddress(new(World, "assets/test/lang/en_us.json", ArtifactKind.Json), new([PathSegment.ForKey(name)]), "language.value");
        Require(SourceAddressJson.Read(SourceAddressJson.Write(address)) == address && NativeJsonStrings.Read(properties[0].Value) == "\udfff"
            && NativeJsonStrings.Read(properties[1].Value) == "雪😀", "native language keys and values remain exact through typed address interchange");
        Reject<JsonException>(() => BoundedJson.ReadDecoded(Encoding.UTF8.GetBytes("{\"\\ud800\":1,\"\\uD800\":2}")));
        Reject<JsonException>(() => BoundedJson.ReadDecoded(Encoding.UTF8.GetBytes("\"\\uZZZZ\"")));
        Reject<JsonException>(() => BoundedJson.ReadDecoded([0x22, 0xed, 0xa0, 0x80, 0x22]));
    }
    public static void JavaStringInputIsLossless()
    {
        var source = "{\"text\":\"雪😀\ud800\\n\\\\\\\"\udfff\"}";
        using var decoded = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(source));
        Require(NativeJsonStrings.Read(decoded.RootElement.GetProperty("text")) == "雪😀\ud800\n\\\"\udfff", "raw Java UTF16 JSON input is transcoded with exact surrogate escapes, never replacement");
        Reject<JsonException>(() => NativeJsonStrings.EncodeJavaDocument("{\"text\":\"\\" + "\ud800\"}"));
        Reject<JsonException>(() => NativeJsonStrings.EncodeJavaDocument("\ud800"));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => NativeJsonStrings.EncodeJavaDocument("\"\ud800\"", new(MaxDecodedBytes: 7)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => NativeJsonStrings.EncodeJavaDocument("\"雪\"", new(MaxDecodedBytes: 4)));
    }
}
