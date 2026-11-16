using System.Text;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class JsonKeyEditsTests
{
    public static void BatchComponentFieldsKeepProtectedBytesAndFraming()
    {
        const string raw = " \r\n{\"text\":\"Root\",\"extra\":[\"Scalar\",{\"\\u0074ext\":\"Child\",\"color\":\"red\"}],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"Root\"},\"n\":1e+02}\t";
        byte[] source = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(raw)];
        var scalar = new TypedPath([PathSegment.ForKey("extra"), PathSegment.ForIndex(0)]); var child = new TypedPath([PathSegment.ForKey("extra"), PathSegment.ForIndex(1)]);
        var output = NativeJsonKeyEdits.Convert(source, [(TypedPath.Root, "Root", "test.root", true), (scalar, "Scalar", "test.scalar", false), (child, "Child", "test.child", true)]);
        const string expected = " \r\n{\"translate\":\"test.root\",\"extra\":[{\"translate\":\"test.scalar\"},{\"translate\":\"test.child\",\"color\":\"red\",\"with\":[\"%\"]}],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"Root\"},\"n\":1e+02,\"with\":[\"%\"]}\t";
        Require(output.SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(expected))), "parent/child and scalar key conversions mutate exact property/value tokens and fixed arguments without reserializing styles/events/numbers/BOM/whitespace");
        var values = NativeJsonValueSpans.Read(source); var number = values[new([PathSegment.ForKey("n")])];
        Require(Encoding.UTF8.GetString(source.AsSpan(number.StartByte, number.LengthBytes)) == "1e+02"
            && values[TypedPath.Root].StartByte == 6 && NativeJsonStringSpans.Read(source).All(item => values[item.Key] == item.Value), "full value spans preserve original container/number offsets and existing string locator behavior");
        var java = NativeJsonKeyEdits.Convert("{\"text\":\"\\ud800\"}"u8.ToArray(), [(TypedPath.Root, "\ud800", "key\udfff", false)]);
        using var parsed = BoundedJson.ReadDecoded(java);
        Require(NativeJsonStrings.Read(parsed.RootElement.GetProperty("translate")) == "key\udfff", "key surgery preserves isolated Java UTF16 code units through JSON escapes");
    }
    public static void RejectedConversionsDoNotGuessOrExpandAuthority()
    {
        var source = "{\"text\":\"Old\",\"extra\":[\"Other\"],\"color\":\"red\"}"u8.ToArray(); var original = source.ToArray();
        foreach (var invalid in new[] { "42", "{}", "{\"text\":42}", "{\"text\":\"Old\",\"translate\":\"key\"}", "{\"text\":\"Old\",\"with\":[]}", "{\"text\":\"Old\",\"fallback\":\"latent\"}", "{\"text\":\"Old\",\"selector\":\"@s\"}" })
            Reject<EditRejectedException>(() => NativeJsonKeyEdits.Convert(Encoding.UTF8.GetBytes(invalid), [(TypedPath.Root, "Old", "key", false)]));
        Reject<EditRejectedException>(() => NativeJsonKeyEdits.Convert(source, [(TypedPath.Root, "stale", "key", false)]));
        Reject<EditRejectedException>(() => NativeJsonKeyEdits.Convert(source, [(new([PathSegment.ForKey("missing")]), "Old", "key", false)]));
        Reject<EditRejectedException>(() => NativeJsonKeyEdits.Convert(source, [(TypedPath.Root, "Old", "a", false), (TypedPath.Root, "Old", "b", false)]));
        Require(NativeJsonKeyEdits.Convert(source, [(TypedPath.Root, "Old", "key", false)], new(MaxDecodedBytes: 200)).Length < 200, "small key conversion budget has a reachable positive control");
        Reject<CodecException>(() => NativeJsonKeyEdits.Convert(source, [(TypedPath.Root, "Old", new string('X', 200), true)], new(MaxDecodedBytes: 200)));
        Require(source.SequenceEqual(original), "accepted/rejected conversions preserve the original source buffer");
    }
}
