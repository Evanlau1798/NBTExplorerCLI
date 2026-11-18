using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Verification;

internal static class NbtModelTests
{
    public static void PreservesTypesAndBits()
    {
        NbtValue[] values = [new NbtByte(-1), new NbtShort(short.MinValue), new NbtInt(int.MinValue),
            new NbtLong(long.MaxValue), new NbtFloat(unchecked((int)0x80000000)), new NbtDouble(long.MinValue),
            new NbtByteArray([]), new NbtString("\0你好\ud800"), new NbtList(NbtType.Long, []),
            new NbtCompound([new("type", new NbtString("custom")), new("value", new NbtString("payload")),
                new("keep", new NbtInt(42))]), new NbtIntArray([]), new NbtLongArray([])];
        Require(values.Select(value => value.Type).SequenceEqual(Enumerable.Range(1, 12).Select(value => (NbtType)value)),
            "所有 payload 型別、空 array kind 及 list header 必須保留");
        Require(((NbtList)values[8]).ElementType == NbtType.Long, "空 list 不能丟失 element type");
        var firstNan = new NbtFloat(unchecked((int)0x7fc00001));
        var secondNan = new NbtFloat(unchecked((int)0x7fc00002));
        Require(float.IsNaN(firstNan.Value) && firstNan != secondNan, "NaN payload 不能被合併");
        Require(values[4] != NbtFloat.FromValue(0f) && values[5] != NbtDouble.FromValue(0d), "負零須保留位元差異");
    }

    public static void RejectsMixedPhysicalList()
    {
        Reject(() => new NbtList(NbtType.Int, [new NbtInt(1), new NbtString("x")]));
        Reject(() => new NbtList(NbtType.End, [new NbtInt(1)]));
        Reject(() => new NbtList((NbtType)255, []));
    }

    public static void StructuralEqualityAndImmutability()
    {
        var input = new List<int> { 1, 2, 3 };
        var first = new NbtIntArray(input);
        input[0] = 99;
        Require(first == new NbtIntArray([1, 2, 3]), "Tree 必須有結構相等及不可變 payload");
        Require(new NbtByteArray([-1]) == new NbtByteArray([-1]), "ByteArray 必須結構相等");
        Require(new NbtLongArray([long.MinValue]) == new NbtLongArray([long.MinValue]), "LongArray 必須結構相等");
        Require(new NbtCompound([new("", first)]) == new NbtCompound([new("", new NbtIntArray([1, 2, 3]))]),
            "Compound entries、空 key 與巢狀值必須結構相等");
    }

    public static void LocatorOutcomes()
    {
        var root = new NbtCompound([new("", new NbtInt(42)), new("dupe", new NbtString("one")),
            new("dupe", new NbtString("two")), new("array", new NbtLongArray([long.MaxValue]))]);
        Require(NbtLocator.Resolve(root, new([PathSegment.ForKey("")])) is { Status: ResolutionStatus.Found, Value: NbtInt { Value: 42 } },
            "空 key 必須精確回解，不能當根路徑");
        Require(NbtLocator.Resolve(root, new([PathSegment.ForKey("dupe")])).Status == ResolutionStatus.Ambiguous,
            "重複 compound key 應保留並回 Ambiguous，不能覆寫或選第一個");
        Require(NbtLocator.Resolve(root, new([PathSegment.ForKey("absent")])).Status == ResolutionStatus.Missing,
            "不存在與非法地址應分開");
        Require(NbtLocator.Resolve(root, new([PathSegment.ForIndex(0)])).Status == ResolutionStatus.Invalid,
            "Compound 不能當 list 索引");
        Require(NbtLocator.Resolve(root, new([PathSegment.ForKey("array"), PathSegment.ForIndex(0)]))
            is { Status: ResolutionStatus.Found, Value: NbtLong { Value: long.MaxValue } }, "Typed array 元素要保留型別");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("實體 list invariant 必須拒絕非法 payload");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
