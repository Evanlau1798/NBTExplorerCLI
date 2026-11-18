using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtTests
{
    internal static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    internal static LogicalMinecraftLeaf Leaf(NbtValue value) => new(value);
    internal static TypedPath Key(string name) => new([PathSegment.ForKey(name)]);
    public static void FrozenF03(string root)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "cases", "format-regressions.json")));
        foreach (var row in cases.RootElement.GetProperty("cases").EnumerateArray().Where(row => row.GetProperty("findings").EnumerateArray().Any(f => f.GetString() == "F03")))
        {
            var bytes = Bytes(row.GetProperty("input").GetString()!);
            if (row.GetProperty("expected").ValueKind == JsonValueKind.String)
            {
                Reject(Enum.Parse<SnbtError>(row.GetProperty("expected").GetString()!), () => Snbt.ParseDocument(bytes)); continue;
            }
            var parsed = Snbt.ParseDocument(bytes);
            if (row.GetProperty("id").GetString() == "SNBT-EMPTY-ARRAYS")
            {
                Require(parsed.Resolve(Key("b")).Value == Leaf(new NbtByteArray([])) && parsed.Resolve(Key("i")).Value == Leaf(new NbtIntArray([]))
                    && parsed.Resolve(Key("l")).Value == Leaf(new NbtLongArray([])), "typed empty arrays不可降為list");
                continue;
            }
            var patched = Snbt.Replace(parsed, Key("text"), Leaf(new NbtString("Hello")), Leaf(new NbtString("Bonjour")));
            var decoded = Snbt.ParseDocument(patched);
            Require(decoded.Resolve(Key("UUID")).Value == Leaf(new NbtIntArray([1, 2, 3, 4]))
                && decoded.Resolve(Key("bytes")).Value == Leaf(new NbtByteArray([-1]))
                && decoded.Resolve(Key("longs")).Value == Leaf(new NbtLongArray([1]))
                && decoded.Resolve(Key("text")).Value == Leaf(new NbtString("Bonjour")), "F03修改文字不得改UUID/typed arrays/signed byte");
        }
    }
    public static void FullVersusFragmentAndByteSpans()
    {
        var text = "say 雪 run tellraw @a { text : 'Hello', keep:42 } then {text:'Hello'}";
        var bytes = Bytes(text);
        var start = Bytes(text[..text.IndexOf('{')]).Length;
        Reject(SnbtError.TrailingInput, () => Snbt.ParseDocument(Bytes("{text:'Hello'}TRAILING")));
        var parsed = Snbt.ParseFragment(bytes, start);
        var target = parsed.Resolve(Key("text"));
        Require(Encoding.UTF8.GetString(bytes.AsSpan(target.Span.StartByte, target.Span.LengthBytes)) == "'Hello'", "span為UTF-8 bytes而非UTF-16index");
        Require(parsed.ConsumedSpan.StartByte == start && parsed.ConsumedSpan.EndByte == Bytes(text[..text.IndexOf(" then", StringComparison.Ordinal)]).Length, "fragment不得吃下一component");
        var patched = Snbt.Replace(parsed, Key("text"), Leaf(new NbtString("Hello")), Leaf(new NbtString("雪\"quote")));
        var delta = patched.Length - bytes.Length;
        Require(patched.AsSpan(0, target.Span.StartByte).SequenceEqual(bytes.AsSpan(0, target.Span.StartByte))
            && patched.AsSpan(target.Span.EndByte + delta).SequenceEqual(bytes.AsSpan(target.Span.EndByte)), "只能變指定span，另一個相同文字component保留原bytes");
        Reject(SnbtError.Precondition, () => Snbt.Replace(parsed, Key("text"), Leaf(new NbtString("stale")), Leaf(new NbtString("new"))));
    }
    public static void ScalarsTypesEscapesAndFailures()
    {
        (string Text, LogicalMinecraftValue Value)[] cases = [
            ("-128b", Leaf(new NbtByte(-128))), ("-32768s", Leaf(new NbtShort(short.MinValue))), ("2147483647", Leaf(new NbtInt(int.MaxValue))),
            ("-9223372036854775808L", Leaf(new NbtLong(long.MinValue))), ("1.25f", Leaf(NbtFloat.FromValue(1.25f))),
            ("-0.0d", Leaf(new NbtDouble(0))), ("true", Leaf(new NbtByte(1))),
            ("'a\\'b'", Leaf(new NbtString("a'b"))), ("\"a\\\\b\"", Leaf(new NbtString("a\\b"))),
            ("[I;-1,2]", Leaf(new NbtIntArray([-1,2]))), ("[B;-1b,127B]", Leaf(new NbtByteArray([-1,127]))),
            ("[L;-1l,2L]", Leaf(new NbtLongArray([-1,2]))), ("[1,2]", new LogicalMinecraftList([Leaf(new NbtInt(1)),Leaf(new NbtInt(2))])) ];
        foreach (var item in cases)
        {
            var parsed = Snbt.ParseDocument(Bytes(item.Text));
            Require(parsed.Root.Value == item.Value && Snbt.ParseDocument(Snbt.Write(item.Value)).Root.Value == item.Value, "scalar/array/list型別與escape需雙向保持");
        }
        Require(Snbt.ParseDocument(Bytes("{'minecraft:key':1,duplicate:3,duplicate:4}"))
            .Root.Value is LogicalMinecraftCompound { Entries.Length: 3 }, "quoted namespace/重複key需保留");
        Reject(SnbtError.Ambiguous, () => Snbt.ParseDocument(Bytes("{a:1,a:2}")).Resolve(Key("a")));
        foreach (var text in new[] { "[I;1b]", "[B;1]", "[L;1]" }) Reject(SnbtError.InvalidArrayElement, () => Snbt.ParseDocument(Bytes(text)));
        Reject(SnbtError.MixedList, () => Snbt.ParseDocument(Bytes("[1,'two']")));
        Reject(SnbtError.InvalidEscape, () => Snbt.ParseDocument(Bytes("'\\n'")));
        Reject(SnbtError.UnexpectedEnd, () => Snbt.ParseDocument(Bytes("'unterminated")));
        Reject(SnbtError.UnsupportedGrammar, () => Snbt.ParseDocument(Bytes("{}"), (SnbtProfile)99));
    }
    public static void ClassicNumericGrammar()
    {
        foreach (var text in new[] { "01", "01b", "1e3", "2147483648", "128b" })
            Require(Snbt.ParseDocument(Bytes(text)).Root.Value == Leaf(new NbtString(text)), "官方1.21.4不匹配數字grammar或overflow時是string: " + text);
        Require(Snbt.ParseDocument(Bytes("1.e3")).Root.Value == Leaf(NbtDouble.FromValue(1000)), "帶小數點的 Java 1.21.4 exponent是double");
        Reject(SnbtError.InvalidToken, () => Snbt.ParseDocument(Bytes("{\"\":1}")));
        Reject(SnbtError.UnsupportedValue, () => Snbt.Write(new LogicalMinecraftCompound([new("", Leaf(new NbtInt(1)))])));
    }
    internal static void Reject(SnbtError code, Action action)
    {
        try { action(); }
        catch (SnbtException error) when (error.Code == code && error.Offset >= 0) { return; }
        throw new InvalidOperationException("Expected SNBT rejection " + code);
    }
}
