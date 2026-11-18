using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json.Nodes;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class TypedNbtInterchangeTests
{
    public static void FrozenFixtures(string root)
    {
        string Read(string kind) => File.ReadAllText(Path.Combine(root, "contracts", "typed-nbt." + kind + ".json"));
        Require(TypedNbtJson.Read(Read("minimal")) == new NbtDocument("", new NbtCompound([])), "最小typed fixture須符合獨立字面值");
        var boundary = TypedNbtJson.Read(Read("boundary"));
        Require(boundary.Name == "\ud800" && boundary.Root is NbtCompound compound
            && compound.Entries.Length == 8 && compound.Entries[3].Value == new NbtFloat(0x7fc01234)
            && compound.Entries[4].Value == new NbtDouble(long.MinValue)
            && compound.Entries[5].Value == new NbtList(NbtType.Long, [])
            && compound.Entries[6].Name == compound.Entries[7].Name, "boundary需保留surrogate/bits/empty list type/duplicate keys");
        Require(TypedNbtJson.Read(TypedNbtJson.Write(boundary)) == boundary, "frozen boundary應雙向保型");
        ExchangeContractTests.RejectJson(() => TypedNbtJson.Read(Read("missing-field")));
        ExchangeContractTests.RejectJson(() => TypedNbtJson.Read(Read("exclusive-state")));
        try { TypedNbtJson.Read(Read("unknown-version")); throw new InvalidOperationException("未知版本必須拒絕"); }
        catch (NotSupportedException) { }
    }
    public static void BinaryAndInterchangeAgree(string root)
    {
        var bytes = BinaryNbtTests.Golden(root);
        var document = BinaryNbt.ReadDocument(bytes).Document;
        var json = TypedNbtJson.Write(document);
        var decoded = TypedNbtJson.Read(json);
        Require(decoded == BinaryNbtTests.Expected && BinaryNbt.WriteDocument(decoded).SequenceEqual(bytes),
            "獨立Java binary→typed interchange→binary需保留所有types/bits/names/list headers/重複key");
        var malformed = JsonNode.Parse(json)!;
        malformed["schemaFamily"] = DataSchemas.DisplayJson;
        try { TypedNbtJson.Read(malformed.ToJsonString()); throw new InvalidOperationException("不可把display JSON當無損interchange"); }
        catch (NotSupportedException) { }
        malformed = JsonNode.Parse(json)!;
        malformed["data"]!.AsObject().Remove("name");
        ExchangeContractTests.RejectJson(() => TypedNbtJson.Read(malformed.ToJsonString()));
    }
    public static void ExplicitTypedValuesAndLimits()
    {
        var document = new NbtDocument("\ud800", new NbtCompound([new("type", new NbtString("custom")),
            new("value", new NbtString("payload")), new("keep", new NbtInt(42)), new("", new NbtList(NbtType.End, []))]));
        Require(TypedNbtJson.Read(TypedNbtJson.Write(document)) == document, "一般type/value是原樣compound，不可猜測或覆蓋空key");
        var json = TypedNbtJson.Write(new("", new NbtString("abc")));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => TypedNbtJson.Read(json, new(MaxMutf8StringBytes: 2)));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => TypedNbtJson.Write(new("", new NbtString(new string('x', 65_536)))));
    }
    public static void WriterRespectsTransportBudget()
    {
        var document = new NbtDocument("", new NbtByteArray(new sbyte[1_000_000]));
        ExchangeContractTests.RejectJson(() => TypedNbtJson.Write(document));
    }
}
