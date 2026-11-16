using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class JsonTextEditsTests
{
    private static TypedPath Path(params PathSegment[] segments) => new(segments);
    public static void ExactTokensPreserveProtectedBytesAndJavaStrings()
    {
        const string original = " \r\n{ \"text\" : \"same\", \"extra\":[{\"text\":\"same\",\"color\":\"gold\",\"clickEvent\":{\"action\":\"run_command\",\"value\":\"same\"}}], \"escaped\":\"\\u0041\", \"n\":1e+02, \"text/key\":[\"other\"] }\r\n";
        byte[] source = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(original)]; var before = source.ToArray();
        var first = "line\n\"\ud800"; const string second = "second{{T0}}|‖§‖";
        var result = NativeJsonEdits.ReplaceStrings(source, [
            (Path(PathSegment.ForKey("text")), "same", first),
            (Path(PathSegment.ForKey("extra"), PathSegment.ForIndex(0), PathSegment.ForKey("text")), "same", second),
            (Path(PathSegment.ForKey("text/key"), PathSegment.ForIndex(0)), "other", "third"),
            (Path(PathSegment.ForKey("escaped")), "A", "A")]);
        var expected = original.Replace("\"text\" : \"same\"", "\"text\" : \"line\\u000a\\\"\\ud800\"", StringComparison.Ordinal)
            .Replace("\"text\":\"same\"", "\"text\":\"" + second + "\"", StringComparison.Ordinal).Replace("[\"other\"]", "[\"third\"]", StringComparison.Ordinal);
        Require(result.SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(expected))) && source.SequenceEqual(before), "only exact authorized string tokens change; BOM, whitespace, property spelling, numeric spelling, events and unchanged escaped tokens keep their bytes");
        using var decoded = BoundedJson.ReadDecoded(result);
        Require(NativeJsonStrings.Read(decoded.RootElement.GetProperty("text")) == first && decoded.RootElement.GetProperty("extra")[0].GetProperty("clickEvent").GetProperty("value").GetString() == "same", "native JSON keeps Java UTF16 code units and protected same-text event values");
        Require(NativeJsonEdits.ReplaceStrings(source, [(Path(PathSegment.ForKey("escaped")), "A", "A")]).SequenceEqual(source), "a decoded no-op preserves the original escape spelling and entire artifact");
        var isolatedKey = Encoding.UTF8.GetBytes("{\"\\ud800\":\"Value\"}");
        Require(Encoding.UTF8.GetString(NativeJsonEdits.ReplaceStrings(isolatedKey, [(Path(PathSegment.ForKey("\ud800")), "Value", "Changed")])) == "{\"\\ud800\":\"Changed\"}", "native Java UTF16 property names retain exact source spelling while selecting their string value");
        Require(Encoding.UTF8.GetString(NativeJsonEdits.ReplaceStrings(Encoding.UTF8.GetBytes("\"scalar\""), [(TypedPath.Root, "scalar", "changed")])) == "\"changed\"", "a scalar component root is one exact string slot");
    }
    public static void InvalidOrUnboundedRequestsCannotProduceCandidates()
    {
        var source = Encoding.UTF8.GetBytes("{\"text\":\"One\",\"n\":1}"); var before = source.ToArray(); var path = Path(PathSegment.ForKey("text"));
        Reject<EditRejectedException>(() => NativeJsonEdits.ReplaceStrings(source, [(path, "Wrong", "Wrong")]));
        Reject<EditRejectedException>(() => NativeJsonEdits.ReplaceStrings(source, [(Path(PathSegment.ForKey("missing")), "One", "Two")]));
        Reject<EditRejectedException>(() => NativeJsonEdits.ReplaceStrings(source, [(Path(PathSegment.ForKey("n")), "1", "Two")]));
        Reject<EditRejectedException>(() => NativeJsonEdits.ReplaceStrings(source, [(path, "One", "Two"), (path, "One", "Three")]));
        Reject<JsonException>(() => NativeJsonEdits.ReplaceStrings(Encoding.UTF8.GetBytes("{\"text\":\"One\",\"text\":\"Other\"}"), [(path, "One", "Two")]));
        Require(NativeJsonEdits.ReplaceStrings(source, [(path, "One", "Two")], new(MaxDecodedBytes: 100)).Length <= 100, "the output-budget negative control reaches a valid source and locator under the same small limit");
        Reject<CodecException>(() => NativeJsonEdits.ReplaceStrings(source, [(path, "One", new string('X', 500))], new(MaxDecodedBytes: 100)));
        Reject<CodecException>(() => NativeJsonEdits.ReplaceStrings(source, [(path, "One", "Two")], new(MaxDecodedBytes: source.Length - 1)));
        Reject<ArgumentException>(() => NativeJsonEdits.ReplaceStrings(source, Enumerable.Repeat((path, "One", "Two"), 100_001)));
        Require(source.SequenceEqual(before), "stale/missing/non-string/duplicate/oversized requests never mutate the source buffer or publish a partial candidate");
    }
    public static void CapacityDependsOnFinalCandidateNotRequestOrder()
    {
        var source = Encoding.UTF8.GetBytes("{\"a\":\"old\",\"b\":\"" + new string('B', 70) + "\",\"n\":1e+02}");
        Require(source.Length <= 100, "bounded source is a positive precondition for the expand/shrink pair");
        (TypedPath Path, string Expected, string Replacement)[] changes = [(Path(PathSegment.ForKey("a")), "old", new string('A', 70)), (Path(PathSegment.ForKey("b")), new string('B', 70), "tiny")];
        var shrinkFirst = NativeJsonEdits.ReplaceStrings(source, changes.Reverse(), new(MaxDecodedBytes: 100));
        var expandFirst = NativeJsonEdits.ReplaceStrings(source, changes, new(MaxDecodedBytes: 100));
        Require(expandFirst.Length <= 100 && expandFirst.SequenceEqual(shrinkFirst), "one complete JSON candidate has identical capacity and bytes regardless of independent request enumeration order");
    }
}
