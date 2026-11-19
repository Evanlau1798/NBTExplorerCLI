using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using static NBTExplorerCLI.Verification.SnbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtModernTests
{
    private const SnbtProfile Modern = SnbtProfile.Java1215;
    public static void ExactNestedEditAndArrayContext()
    {
        var source = Bytes("say 雪 run tellraw @a {text:'same',extra:[1,{text:'same'}],uuid:uuid('1-1-1-1-1')}\r\nthen {text:'same'}");
        var start = Encoding.UTF8.GetString(source).IndexOf('{');
        start = Bytes(Encoding.UTF8.GetString(source)[..start]).Length;
        var document = Snbt.ParseFragment(source, start, Modern);
        var path = new TypedPath([PathSegment.ForKey("extra"), PathSegment.ForIndex(1), PathSegment.ForKey("text")]);
        var span = document.Resolve(path).Span;
        var replacement = Leaf(new NbtString("new\n\ud800\U00020000"));
        var changed = Snbt.Replace(document, path, Leaf(new NbtString("same")), replacement);
        var delta = changed.Length - source.Length;
        Require(changed.AsSpan(0, span.StartByte).SequenceEqual(source.AsSpan(0, span.StartByte))
            && changed.AsSpan(span.EndByte + delta).SequenceEqual(source.AsSpan(span.EndByte)), "modern異型list中的文字只能改指定byte span");
        Require(Snbt.ParseFragment(changed, start, Modern).Resolve(path).Value == replacement, "modern escaped UTF-16需保型");
        var array = Snbt.ParseDocument(Bytes("[I;1b,2s,3]"), Modern);
        var index = new TypedPath([PathSegment.ForIndex(0)]);
        Require(array.Resolve(index).Value == Leaf(new NbtInt(1)), "typed array child為promoted有效型別");
        var output = Snbt.Replace(array, index, Leaf(new NbtInt(1)), Leaf(new NbtInt(int.MinValue)));
        Require(Snbt.ParseDocument(output, Modern).Root.Value == Leaf(new NbtIntArray([int.MinValue, 2, 3])), "array child splice保持kind與其他值");
        try { Snbt.Replace(array, index, Leaf(new NbtInt(1)), Leaf(new NbtLong(4))); throw new InvalidOperationException("Array narrowing must reject"); }
        catch (SnbtException error) when (error.Code == SnbtError.InvalidArrayElement)
        {
            Require(error.Offset == array.Resolve(index).Span.StartByte, "patch type diagnostic必須指向實際array element byte offset");
        }
    }
    public static void UnicodeAndLimits()
    {
        (string Name, string Value)[] names = [("latin small letter e with acute", "\u00e9"), ("CJK UNIFIED IDEOGRAPHS 4E00", "一"),
            ("HIGH SURROGATES D800", "\ud800"), ("PRIVATE USE AREA E000", "\ue000"), ("NULL", "\0")];
        foreach (var item in names)
            Require(Snbt.ParseDocument(Bytes("'\\N{" + item.Name + "}'"), Modern).Root.Value == Leaf(new NbtString(item.Value)), "固定Java21 Unicode15 name table與code units需一致");
        Reject(SnbtError.InvalidEscape, () => Snbt.ParseDocument(Bytes("'\\Uffffffff'"), Modern));
        Reject(SnbtError.InvalidEscape, () => Snbt.ParseDocument(Bytes("'\\N{not a name}'"), Modern));
        Reject(SnbtError.LimitExceeded, () => Snbt.ParseDocument(Bytes("'\\U00020000'"), Modern, new(MaxMutf8StringBytes: 5)));
        Require(Snbt.ParseDocument(Bytes("'\\U00020000'"), Modern, new(MaxMutf8StringBytes: 6)).Root.Value == Leaf(new NbtString("\U00020000")), "supplementary codepoint為兩個MUTF8 code units");
        Reject(SnbtError.LimitExceeded, () => Snbt.ParseDocument(Bytes("bool(0)"), Modern, new(MaxNodes: 1)));
        Reject(SnbtError.UnsupportedValue, () => Snbt.Write(Leaf(NbtDouble.FromValue(double.PositiveInfinity)), Modern));
        Reject(SnbtError.InvalidNumber, () => Snbt.ParseDocument(Bytes("18446744073709551616ul"), Modern));
    }
}
