using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class MinecraftNbtAdapterTests
{
    internal static CapabilityProfile Profile(LogicalListEncoding encoding = LogicalListEncoding.CompoundWrapper, string? key = "")
        => new("java-1.21.5-nbt", 1, [], new(encoding, encoding == LogicalListEncoding.CompoundWrapper ? key : null));
    private static LogicalMinecraftLeaf Int(int value) => new(new NbtInt(value));
    private static LogicalMinecraftLeaf Text(string value) => new(new NbtString(value));
    private static LogicalMinecraftCompound Compound(string name, LogicalMinecraftValue value) => new([new(name, value)]);
    public static void MojangOracle(string root)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "codec", "minecraft-list-oracle.json")));
        Require(oracle.RootElement.GetProperty("unwrapWitness").GetBoolean(), "官方方法須確認單層解包witness");
        var expected = new Dictionary<string, LogicalMinecraftList>
        {
            ["official-example"] = new([Text("a"), Compound("b", Int(3))]),
            ["sole-empty-key-escaped"] = new([Compound("", Int(42))]),
            ["nested-mixed"] = new([new LogicalMinecraftList([Int(1), Text("two")]), Compound("", Int(42))]),
            ["homogeneous-int"] = new([Int(-1), Int(42)]),
            ["empty"] = new([])
        };
        foreach (var fixture in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            Require(fixture.GetProperty("readbackWitness").GetBoolean(), "官方完整readback也須還原原logical list");
            var bytes = fixture.GetProperty("base64").GetBytesFromBase64();
            Require(Hash256.Compute(bytes).Hex.Equals(fixture.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Mojang oracle hash");
            var physical = BinaryNbt.ReadDocument(bytes).Document;
            var logical = new LogicalMinecraftDocument("", expected[fixture.GetProperty("id").GetString()!]);
            Require(MinecraftNbtAdapter.Decode(physical, Profile()) == logical, "實際Mojang bytes須遞迴解包且保留合法空key");
            var encoded = MinecraftNbtAdapter.Encode(logical, Profile());
            Require(BinaryNbt.WriteDocument(encoded).SequenceEqual(bytes), "邏輯值寫入須符合官方bytes而非自證");
            Require(MinecraftNbtAdapter.Decode(encoded, Profile()) == logical, "雙向logical語義不可變");
        }
    }
    public static void AllTypesAndWrapperShapes(string root)
    {
        var golden = BinaryNbtTests.Expected;
        Require(MinecraftNbtAdapter.Encode(MinecraftNbtAdapter.Decode(golden, Profile()), Profile()) == golden,
            "全部NBT型別、arrays、bits及空list header經logical tree亦須可逆");
        NbtValue[] values = [new NbtByte(-1), new NbtShort(-2), new NbtInt(3), new NbtLong(4), new NbtFloat(0x7fc01234),
            new NbtDouble(long.MinValue), new NbtByteArray([-1]), new NbtString("\0\ud800"), new NbtIntArray([1]), new NbtLongArray([2])];
        var shapes = values.Select(value => (LogicalMinecraftValue)new LogicalMinecraftLeaf(value)).ToList();
        shapes.Add(new LogicalMinecraftCompound([]));
        shapes.Add(Compound("", Int(1)));
        shapes.Add(Compound("", Compound("", Text("keep"))));
        shapes.Add(new LogicalMinecraftCompound([new("", Int(1)), new("", Int(2))]));
        shapes.Add(new LogicalMinecraftCompound([new("", Int(1)), new("keep", Text("two"))]));
        shapes.Add(new LogicalMinecraftList([], NbtType.Short));
        foreach (var first in shapes)
            foreach (var second in shapes)
            {
                var logical = new LogicalMinecraftDocument("", new LogicalMinecraftList([first, second]));
                var physical = MinecraftNbtAdapter.Encode(logical, Profile());
                var bytes = BinaryNbt.WriteDocument(physical);
                Require(MinecraftNbtAdapter.Decode(BinaryNbt.ReadDocument(bytes).Document, Profile()) == logical,
                    "固定型別／wrapper形狀交叉組合須保留雙向語義");
            }
        Require(BinaryNbt.WriteDocument(MinecraftNbtAdapter.Encode(MinecraftNbtAdapter.Decode(golden, Profile()), Profile()))
            .SequenceEqual(BinaryNbtTests.Golden(root)), "logical roundtrip亦須符合完整Java binary fixture");
    }
    public static void OrdinaryCompoundsAndTypedEmptyLists()
    {
        var physical = new NbtDocument("\ud800", new NbtCompound([
            new("", new NbtInt(42)), new("type", new NbtString("custom")), new("value", new NbtString("payload")),
            new("keep", new NbtIntArray([1, 2])), new("empty", new NbtList(NbtType.Long, [])),
            new("list", new NbtList(NbtType.Compound, [new NbtCompound([new("", new NbtInt(1)), new("", new NbtInt(2))])]))]));
        var logical = MinecraftNbtAdapter.Decode(physical, Profile());
        Require(logical.Root is LogicalMinecraftCompound c && c.Entries.Length == 6
            && c.Entries[0].Name == "" && c.Entries[0].Value == Int(42), "普通compound不解包、不合併key");
        Require(MinecraftNbtAdapter.Encode(logical, Profile()) == physical, "空list type、重複key、一般type/value/keep需保型");
        var unescaped = new NbtDocument("", new NbtList(NbtType.Compound, [new NbtCompound([new("", new NbtInt(42))])]));
        Require(MinecraftNbtAdapter.Decode(unescaped, Profile()).Root == new LogicalMinecraftList([Int(42)]), "只有list element且恰一個空key解包一次");
        var homogeneous = Profile(LogicalListEncoding.Homogeneous);
        Require(MinecraftNbtAdapter.Encode(MinecraftNbtAdapter.Decode(unescaped, homogeneous), homogeneous) == unescaped, "homogeneous profile不得套新版wrapper語義");
    }
    public static void ProfileAndLimits()
    {
        var logical = new LogicalMinecraftDocument("", new LogicalMinecraftList([Int(1), Text("two")]));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => MinecraftNbtAdapter.Encode(logical, Profile(LogicalListEncoding.Unknown)));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => MinecraftNbtAdapter.Encode(logical, Profile(key: "guess")));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => MinecraftNbtAdapter.Encode(logical, Profile(LogicalListEncoding.Homogeneous)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => MinecraftNbtAdapter.Encode(logical, Profile(), new(MaxDepth: 1)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => MinecraftNbtAdapter.Encode(logical, Profile(), new(MaxNodes: 4)));
        var physical = new NbtDocument("", new NbtList(NbtType.Int, [new NbtInt(1)]));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => MinecraftNbtAdapter.Decode(physical, Profile(), new(MaxNodes: 1)));
        var input = new LogicalMinecraftValue[] { Int(1) };
        var immutable = new LogicalMinecraftList(input); input[0] = Int(2);
        Require(immutable.Items[0] == Int(1), "logical tree需持有不可變snapshot");
        LogicalMinecraftValue deep = Int(1);
        for (var i = 0; i < 257; i++) deep = new LogicalMinecraftList([deep]);
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => MinecraftNbtAdapter.Encode(new("", deep), Profile()));
    }
}
