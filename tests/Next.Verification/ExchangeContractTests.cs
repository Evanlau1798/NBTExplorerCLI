using Next.Domain;
using Next.Formats;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class ExchangeContractTests
{
    public static void FrozenFixtures(string fixtureRoot)
    {
        Func<string, object>[] decoders = [json => SourceAddressJson.Read(json), json => TextSlotJson.Read(json), json => TypedEditJson.Read(json)];
        var families = new[] { "source-address", "text-slot", "typed-edit" };
        for (var index = 0; index < families.Length; index++)
        {
            var decode = decoders[index];
            string Read(string name) => File.ReadAllText(Path.Combine(fixtureRoot, "contracts", families[index] + "." + name + ".json"));
            _ = decode(Read("minimal"));
            _ = decode(Read("boundary"));
            RejectJson(() => decode(Read("missing-field")));
            RejectJson(() => decode(Read("exclusive-state")));
            try { decode(Read("unknown-version")); throw new InvalidOperationException("未知schema版本fixture未拒絕"); }
            catch (NotSupportedException) { }
        }
    }

    public static void SlotRoundtripAndClaims()
    {
        var original = Slot("\0\ud800‖§‖ {{T0}} %s", true);
        var json = TextSlotJson.Write(original);
        var decoded = TextSlotJson.Read(json);
        Require(decoded.Id == original.Id && decoded.SourceText == original.SourceText && decoded.SourceTextHash == original.SourceTextHash
            && decoded.Context == original.Context && decoded.AllowedEmpty && decoded.Placeholders.Tokens.SequenceEqual(original.Placeholders.Tokens),
            "slot交換需保留完整地址、decoded字串、revision與限制");
        var forged = JsonNode.Parse(json)!;
        forged["data"]!["sourceTextHash"] = new string('0', 64);
        RejectJson(() => TextSlotJson.Read(forged.ToJsonString()));
        ValidateEnvelope(json, TextSlotJson.Read);
    }

    public static void TypedEditRoundtripAndTypes()
    {
        NbtValue[] values = [new NbtByte(-1), new NbtShort(short.MinValue), new NbtInt(int.MinValue), new NbtLong(long.MaxValue),
            new NbtFloat(unchecked((int)0x7fc00001)), new NbtDouble(long.MinValue), new NbtByteArray([-1, 0]), new NbtString("\ud800\0你好"),
            new NbtList(NbtType.Long, []), new NbtCompound([new("type", new NbtString("custom")), new("value", new NbtString("payload")),
                new("", new NbtInt(42)), new("duplicate", new NbtByte(1)), new("duplicate", new NbtShort(2))]), new NbtIntArray([]), new NbtLongArray([long.MinValue])];
        foreach (var value in values)
        {
            var edit = TypedEdit.Replace(new([PathSegment.ForKey("/~\ud800")]), value, value);
            var decoded = TypedEditJson.Read(TypedEditJson.Write(edit));
            Require(decoded.Expected == value && decoded.Replacement == value && decoded.Path.Equals(edit.Path), "TypedEdit交換不可丟型別/NaN payload/空list type/重複key");
        }
        var add = TypedEdit.Add(TypedPath.Root, new NbtCompound([]), PathSegment.ForKey(""), new NbtInt(42));
        Require(TypedEditJson.Read(TypedEditJson.Write(add)).Child == PathSegment.ForKey(""), "add需要保存顯式child selector");
        var rename = TypedEdit.Rename(new([PathSegment.ForKey("old")]), new NbtLong(1), "");
        Require(TypedEditJson.Read(TypedEditJson.Write(rename)).NewName == "", "rename合法空key不能當未提供");
        var json = TypedEditJson.Write(TypedEdit.Remove(new([PathSegment.ForIndex(0)]), new NbtInt(1)));
        var invalid = JsonNode.Parse(json)!;
        invalid["data"]!["replacement"] = JsonNode.Parse("{\"tag\":\"int\",\"value\":2}");
        RejectJson(() => TypedEditJson.Read(invalid.ToJsonString()));
        ValidateEnvelope(json, TypedEditJson.Read);
    }

    private static void ValidateEnvelope<T>(string json, Func<string, T> read)
    {
        var envelope = JsonNode.Parse(json)!;
        envelope["schemaVersion"] = 99;
        try { read(envelope.ToJsonString()); throw new InvalidOperationException("Unknown version accepted"); }
        catch (NotSupportedException) { }
        envelope = JsonNode.Parse(json)!;
        envelope.AsObject().Remove("schemaVersion");
        RejectJson(() => read(envelope.ToJsonString()));
        envelope = JsonNode.Parse(json)!;
        envelope["unexpected"] = true;
        RejectJson(() => read(envelope.ToJsonString()));
        RejectJson(() => read(json.Insert(1, "\"schemaVersion\":1,")));
        envelope = JsonNode.Parse(json)!;
        envelope["data"] = null;
        RejectJson(() => read(envelope.ToJsonString()));
    }

    internal static void RejectJson(Action action)
    {
        try { action(); }
        catch (JsonException) { return; }
        throw new InvalidOperationException("Malformed contract必須回JsonException");
    }
}
