using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class BinaryNbtTests
{
    internal static NbtDocument Expected => new("root\0", new NbtCompound([
        new("byte", new NbtByte(-128)), new("short", new NbtShort(short.MinValue)), new("int", new NbtInt(int.MaxValue)),
        new("long", new NbtLong(long.MinValue)), new("float", new NbtFloat(0x7fc01234)), new("double", new NbtDouble(long.MinValue)),
        new("bytes", new NbtByteArray([-128, -1, 0, 127])), new("string", new NbtString("zero\0\u0800\ud800\ud83d\ude00")),
        new("list", new NbtList(NbtType.Int, [new NbtInt(-1), new NbtInt(0), new NbtInt(int.MaxValue)])),
        new("empty", new NbtList(NbtType.Long, [])),
        new("compound", new NbtCompound([new("type", new NbtString("custom")), new("value", new NbtString("payload")),
            new("", new NbtInt(42)), new("duplicate", new NbtShort(7)), new("duplicate", new NbtShort(8))])),
        new("ints", new NbtIntArray([int.MinValue, -1, 0, int.MaxValue])),
        new("longs", new NbtLongArray([long.MinValue, -1, 0, long.MaxValue]))]));
    internal static byte[] Golden(string root)
    {
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "codec", "java-oracle.json")));
        var nbt = oracle.RootElement.GetProperty("nbt");
        var bytes = nbt.GetProperty("base64").GetBytesFromBase64();
        Require(bytes.Length == nbt.GetProperty("bytes").GetInt32() && Hash256.Compute(bytes).Hex.Equals(nbt.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase),
            "獨立Java fixture身份必須相符");
        return bytes;
    }
    public static void IndependentJavaBytes(string root)
    {
        var bytes = Golden(root);
        var result = BinaryNbt.ReadDocument(bytes);
        Require(result.Document == Expected, "獨立Java DataOutput fixture必須解碼全部tag、raw浮點bits、重複/空keys");
        Require(result.ConsumedBytes == bytes.Length && result.Metrics is { Nodes: 22, ContainerEntries: 18, CollectionElements: 15, StringBytes: 128, MaximumDepth: 2 },
            "一次來源的累積ledger不能在子樹重設");
        Require(BinaryNbt.WriteDocument(Expected).SequenceEqual(bytes), "新writer需符合獨立Java bytes，不只自讀roundtrip");
        Require(BinaryNbt.Validate(bytes) == result.Metrics, "Validate與Read必須同一語義與ledger");
    }
    public static void ShortStreamAndOwnership(string root)
    {
        var bytes = Golden(root);
        using var input = new OneByteStream(bytes);
        Require(BinaryNbt.ReadDocument(input).Document == Expected && input.CanRead, "非seekable短讀必須完整且不關閉caller stream");
        using var output = new MemoryStream();
        Require(BinaryNbt.WriteDocument(Expected, output).Bytes == bytes.Length && output.CanWrite && output.ToArray().SequenceEqual(bytes), "caller持有輸出stream，不因Dispose暗中保存或關閉");
    }
    public static void InvalidWireAndTrailing(string root)
    {
        (byte[] Bytes, CodecError Error)[] cases =
        [
            ([], CodecError.Truncated), ([0], CodecError.InvalidTag), ([13, 0, 0], CodecError.InvalidTag),
            ([7, 0, 0, 255, 255, 255, 255], CodecError.InvalidLength),
            ([7, 0, 0, 127, 255, 255, 255], CodecError.LimitExceeded),
            ([9, 0, 0, 0, 0, 0, 0, 1], CodecError.InvalidList),
            ([8, 0, 0, 0, 1, 0xc2], CodecError.MalformedString),
            (Golden(root).Concat(new byte[] { 99 }).ToArray(), CodecError.TrailingData)
        ];
        foreach (var (bytes, error) in cases)
        {
            RejectCodec(error, () => BinaryNbt.ReadDocument(bytes));
            using var stream = new OneByteStream(bytes);
            RejectCodec(error, () => BinaryNbt.ReadDocument(stream));
        }
        var endList = new NbtDocument("", new NbtList(NbtType.End, []));
        Require(BinaryNbt.ReadDocument([9, 0, 0, 0, 0, 0, 0, 0]).Document == endList, "空list保留End header，不可當payload End");
        RejectCodec(CodecError.UnsupportedProfile, () => BinaryNbt.ReadDocument(Golden(root), (BinaryNbtProfile)99));
    }
    public static void CumulativeLimitBoundaries(string root)
    {
        var bytes = Golden(root);
        var exact = new DecodeLimits(MaxDecodedBytes: bytes.Length, MaxDepth: 2, MaxNodes: 22, MaxContainerEntries: 18,
            MaxCollectionElements: 15, MaxStringBytes: 128, MaxMutf8StringBytes: 18);
        Require(BinaryNbt.ReadDocument(bytes, limits: exact).Document == Expected, "所有ledger恰在界線時應接受");
        var exceeded = new[] { exact with { MaxDecodedBytes = bytes.Length - 1 }, exact with { MaxDepth = 1 }, exact with { MaxNodes = 21 },
            exact with { MaxContainerEntries = 17 }, exact with { MaxCollectionElements = 14 }, exact with { MaxStringBytes = 127 }, exact with { MaxMutf8StringBytes = 17 } };
        foreach (var limits in exceeded)
        {
            RejectCodec(CodecError.LimitExceeded, () => BinaryNbt.ReadDocument(bytes, limits: limits));
            RejectCodec(CodecError.LimitExceeded, () => BinaryNbt.WriteDocument(Expected, limits));
        }
    }
    public static void WriterPreflightPreservesOutput()
    {
        using var output = new MemoryStream();
        output.Write([9, 9, 9]); output.Position = 1;
        var tooLong = new NbtDocument("", new NbtString(new string('\0', 32_768)));
        RejectCodec(CodecError.LimitExceeded, () => BinaryNbt.WriteDocument(tooLong, output));
        Require(output.Position == 1 && output.ToArray().SequenceEqual(new byte[] { 9, 9, 9 }), "validation失敗前不得寫入任何prefix");
    }
    public static void EveryTruncatedPrefixAndRoots(string root)
    {
        var golden = Golden(root);
        for (var count = 0; count < golden.Length; count++)
        {
            var bytes = golden[..count];
            RejectCodec(CodecError.Truncated, () => BinaryNbt.ReadDocument(bytes));
            using var stream = new OneByteStream(bytes);
            RejectCodec(CodecError.Truncated, () => BinaryNbt.ReadDocument(stream));
        }
        foreach (var entry in ((NbtCompound)Expected.Root).Entries)
        {
            var document = new NbtDocument("\ud800", entry.Value);
            var bytes = BinaryNbt.WriteDocument(document);
            using var stream = new OneByteStream(bytes);
            Require(BinaryNbt.ReadDocument(bytes).Document == document && BinaryNbt.ReadDocument(stream).Document == document,
                "每個payload都可作named root，buffer/stream須一致");
        }
        var nested = (NbtValue)new NbtCompound([]);
        for (var depth = 0; depth < 257; depth++) nested = new NbtCompound([new("", nested)]);
        RejectCodec(CodecError.LimitExceeded, () => BinaryNbt.WriteDocument(new("", nested)));
    }
    public static void FrozenF02Cases(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "cases", "format-regressions.json")));
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("findings").EnumerateArray().Any(finding => finding.GetString() == "F02")))
        {
            var bytes = item.GetProperty("inputBase64").GetBytesFromBase64();
            if (item.GetProperty("expected").ValueKind == JsonValueKind.String) RejectCodec(CodecError.MalformedString, () => BinaryNbt.ReadDocument(bytes));
            else
            {
                var value = (NbtString)NbtLocator.Resolve(BinaryNbt.ReadDocument(bytes).Document.Root, new([PathSegment.ForKey("s")])).Value!;
                Require(value.Value.Select(unit => (int)unit).SequenceEqual(item.GetProperty("expected").GetProperty("sUtf16").EnumerateArray()
                    .Select(unit => unit.GetInt32())), "F02合法代理對需保留Java code units");
            }
        }
    }
    public static void HardLimitPolicy()
    {
        var policy = DecodeLimits.Default;
        var invalid = new[] { policy with { MaxDecodedBytes = policy.MaxDecodedBytes + 1 }, policy with { MaxDepth = 257 },
            policy with { MaxNodes = policy.MaxNodes + 1 }, policy with { MaxContainerEntries = policy.MaxContainerEntries + 1 },
            policy with { MaxCollectionElements = policy.MaxCollectionElements + 1 }, policy with { MaxStringBytes = policy.MaxStringBytes + 1 },
            policy with { MaxCompressedBytes = policy.MaxCompressedBytes + 1 }, policy with { MaxMutf8StringBytes = 65_536 } };
        foreach (var limits in invalid) Reject(limits.Validate);
        new DecodeLimits(MaxDecodedBytes: 8, MaxDepth: 0, MaxNodes: 1, MaxContainerEntries: 0, MaxCollectionElements: 0, MaxStringBytes: 0, MaxMutf8StringBytes: 0).Validate();
    }
    internal static void RejectCodec(CodecError expected, Action action)
    {
        try { action(); }
        catch (CodecException error) when (error.Code == expected && error.Offset >= 0) { return; }
        throw new InvalidOperationException("Expected codec rejection " + expected);
    }
    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    }
}
