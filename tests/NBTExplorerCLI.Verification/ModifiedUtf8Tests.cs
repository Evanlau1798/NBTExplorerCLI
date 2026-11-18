using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class ModifiedUtf8Tests
{
    public static void JavaOracle(string fixtureRoot)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "codec", "java-oracle.json")));
        foreach (var item in oracle.RootElement.GetProperty("mutf8").EnumerateArray())
        {
            var framed = item.GetProperty("framedBase64").GetBytesFromBase64();
            var accepted = item.GetProperty("accepted").GetBoolean();
            try
            {
                var value = ModifiedUtf8.Decode(framed.AsSpan(2));
                Require(accepted, "Java拒絕的MUTF8不應被接受");
                var units = Convert.FromHexString(item.GetProperty("utf16be").GetString()!);
                var expected = new char[units.Length / 2];
                for (var index = 0; index < expected.Length; index++) expected[index] = (char)((units[index * 2] << 8) | units[index * 2 + 1]);
                Require(value == new string(expected), "C#與Java readUTF code units不符");
            }
            catch (CodecException error) when (!accepted && error.Code == CodecError.MalformedString) { }
        }
        foreach (var item in oracle.RootElement.GetProperty("boundaries").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var pieces = id.Split('-');
            var value = new string(pieces[0] == "nul" ? '\0' : 'a', int.Parse(pieces[1]));
            try
            {
                var encoded = ModifiedUtf8.Encode(value);
                Require(item.GetProperty("accepted").GetBoolean() && encoded.Length == item.GetProperty("bytes").GetInt32()
                    && Hash256.Compute(encoded).Hex.Equals(item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Java writeUTF邊界或bytes hash不符");
            }
            catch (CodecException error) when (!item.GetProperty("accepted").GetBoolean() && error.Code == CodecError.LimitExceeded) { }
        }
    }
    public static void JavaCodeUnitEncoding()
    {
        var text = "\0\u0001\u007f\u0080\u07ff\u0800\ud800\udc00\uffff\ud83d\ude00";
        var expected = Convert.FromHexString("C080017FC280DFBFE0A080EDA080EDB080EFBFBFEDA0BDEDB880");
        Require(ModifiedUtf8.Encode(text).SequenceEqual(expected), "MUTF-8需依UTF-16 code units編碼NUL/BMP/surrogate，不能使用普通UTF-8");
        Require(ModifiedUtf8.Decode(expected) == text && ModifiedUtf8.EncodedLength(text) == expected.Length, "code units需完整可逆");
        Require(ModifiedUtf8.Decode([0, 0xc0, 0x81, 0xe0, 0x80, 0x80]) == "\0\u0001\0", "Java readUTF允許raw NUL及其解碼演算法接受的非最短形式");
    }
    public static void RejectsMalformedSequences()
    {
        byte[][] invalid = [[0xc2], [0xc2, 0x41], [0xe0, 0x80], [0xe0, 0x80, 0x41], [0x80], [0xff], [0xf0, 0x9f, 0x98, 0x80]];
        foreach (var bytes in invalid)
        {
            try { ModifiedUtf8.Decode(bytes, 17); }
            catch (CodecException error) when (error.Code == CodecError.MalformedString && error.Offset >= 17 && error.Offset < 17 + bytes.Length) { continue; }
            throw new InvalidOperationException("非法MUTF8需回MalformedString與來源byte offset");
        }
    }
    public static void EncodedByteLimits()
    {
        Require(ModifiedUtf8.Encode(new string('a', 65_535)).Length == 65_535, "65535 encoded bytes合法");
        Require(ModifiedUtf8.Encode(new string('\0', 32_767)).Length == 65_534, "NUL是兩bytes，不是字元數上限");
        foreach (var text in new[] { new string('a', 65_536), new string('\0', 32_768), new string('\ud800', 21_846) })
        {
            try { ModifiedUtf8.Encode(text); }
            catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { continue; }
            throw new InvalidOperationException("encoded length必須在allocation前拒絕超限");
        }
        Require(ModifiedUtf8.Encode("", 0).Length == 0, "空字串可在零string budget表示");
    }
}
