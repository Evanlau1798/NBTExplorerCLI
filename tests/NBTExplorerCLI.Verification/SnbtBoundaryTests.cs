using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.SnbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtBoundaryTests
{
    public static void ResourceBoundaries()
    {
        var source = Bytes("{a:[1,2],b:'x'}");
        var exact = new DecodeLimits(MaxDecodedBytes: source.Length, MaxDepth: 2, MaxNodes: 5, MaxContainerEntries: 2,
            MaxCollectionElements: 2, MaxStringBytes: 3, MaxMutf8StringBytes: 1);
        var parsed = Snbt.ParseDocument(source, limits: exact);
        var exceeded = new[] { exact with { MaxDecodedBytes = source.Length - 1 }, exact with { MaxDepth = 1 }, exact with { MaxNodes = 4 },
            exact with { MaxContainerEntries = 1 }, exact with { MaxCollectionElements = 1 }, exact with { MaxStringBytes = 2 } };
        foreach (var limits in exceeded) Reject(SnbtError.LimitExceeded, () => Snbt.ParseDocument(source, limits: limits));
        Reject(SnbtError.LimitExceeded, () => Snbt.ParseDocument(Bytes("'😀'"), limits: new(MaxMutf8StringBytes: 1)));
        Require(Snbt.ParseDocument(Bytes("42"), limits: new(MaxStringBytes: 0, MaxMutf8StringBytes: 0)).Root.Value == Leaf(new NbtInt(42)),
            "數字字面值不應消耗decoded string budget");
        var canonical = Snbt.Write(parsed.Root.Value);
        var writerExact = exact with { MaxDecodedBytes = canonical.Length };
        Require(Snbt.Write(parsed.Root.Value, limits: writerExact).SequenceEqual(canonical), "writer exact quotas應接受");
        Reject(SnbtError.LimitExceeded, () => Snbt.Write(parsed.Root.Value, limits: writerExact with { MaxNodes = 4 }));
        Reject(SnbtError.LimitExceeded, () => Snbt.Write(parsed.Root.Value, limits: writerExact with { MaxDecodedBytes = canonical.Length - 1 }));
        Reject(SnbtError.LimitExceeded, () => Snbt.ParseDocument(Bytes(new string('[', 257) + "0" + new string(']', 257))));
    }
    public static void SnapshotArraysAndUnrepresentableValues()
    {
        var source = Bytes("{a:[I;1,2],text:'x'}");
        var parsed = Snbt.ParseDocument(source); source[0] = (byte)'X';
        var path = new TypedPath([PathSegment.ForKey("a"), PathSegment.ForIndex(1)]);
        var changed = Snbt.Replace(parsed, path, Leaf(new NbtInt(2)), Leaf(new NbtInt(3)));
        Require(Snbt.ParseDocument(changed).Resolve(Key("a")).Value == Leaf(new NbtIntArray([1, 3])) && changed[0] == '{', "AST snapshot不可受caller array變更，typed array元素精確替換保持array kind");
        Reject(SnbtError.UnsupportedValue, () => Snbt.Write(new LogicalMinecraftList([], NbtType.Long)));
        Reject(SnbtError.UnsupportedValue, () => Snbt.Write(Leaf(new NbtFloat(0x7fc01234))));
        Reject(SnbtError.UnsupportedValue, () => Snbt.Write(Leaf(new NbtString("\ud800"))));
        Reject(SnbtError.InvalidToken, () => Snbt.ParseDocument([0xff]));
        Reject(SnbtError.Missing, () => parsed.Resolve(Key("missing")));
        var boundary = Snbt.ParseFragment(Bytes("'x'b"), 0);
        Reject(SnbtError.Precondition, () => Snbt.Replace(boundary, TypedPath.Root, Leaf(new NbtString("x")), Leaf(new NbtInt(1))));
    }
    public static void ClassicInfinitiesAndContainerEdits()
    {
        LogicalMinecraftValue[] values = [Leaf(NbtFloat.FromValue(float.PositiveInfinity)), Leaf(NbtFloat.FromValue(float.NegativeInfinity)),
            Leaf(NbtDouble.FromValue(double.PositiveInfinity)), Leaf(NbtDouble.FromValue(double.NegativeInfinity))];
        foreach (var value in values) Require(Snbt.ParseDocument(Snbt.Write(value)).Root.Value == value, "Java 1.21.4 語法可用溢位指數表示無窮大，並保留型別與正負號");
        var array = Snbt.ParseDocument(Bytes("[I;1,2]"));
        Reject(SnbtError.InvalidArrayElement, () => Snbt.Replace(array, new([PathSegment.ForIndex(0)]), Leaf(new NbtInt(1)), Leaf(new NbtString("bad"))));
        var list = Snbt.ParseDocument(Bytes("[1,2]"));
        Reject(SnbtError.MixedList, () => Snbt.Replace(list, new([PathSegment.ForIndex(0)]), Leaf(new NbtInt(1)), Leaf(new NbtString("bad"))));
    }
    public static void NegativeZeroIsNotRepresentable()
    {
        foreach (var profile in Enum.GetValues<SnbtProfile>())
        {
            Reject(SnbtError.UnsupportedValue, () => Snbt.Write(Leaf(new NbtFloat(int.MinValue)), profile));
            Reject(SnbtError.UnsupportedValue, () => Snbt.Write(Leaf(new NbtDouble(long.MinValue)), profile));
        }
    }
}
