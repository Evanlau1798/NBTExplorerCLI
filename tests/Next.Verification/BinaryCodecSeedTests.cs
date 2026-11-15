using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class BinaryCodecSeedTests
{
    public static void TreesAndMalformed()
    {
        var random = new Random(902061);
        for (var index = 0; index < 200; index++)
        {
            var document = new NbtDocument(index % 2 == 0 ? "root\0\ud800" : "", Tree(random, 0));
            var binary = BinaryNbt.WriteDocument(document);
            Require(BinaryNbt.ReadDocument(binary).Document == document, "seeded trees preserve tag types, code units and raw bits");
            foreach (var codec in new[] { NbtCompression.GZip, NbtCompression.Zlib })
                Require(CompressedNbt.ReadDocument(CompressedNbt.WriteDocument(document, codec), codec).Document == document, "composition agrees for generated scalar/container roots");
            for (var trial = 0; trial < 4; trial++)
            {
                var malformed = binary.ToArray(); malformed[random.Next(malformed.Length)] ^= (byte)(1 << random.Next(8));
                try { _ = BinaryNbt.ReadDocument(malformed, limits: new(MaxDecodedBytes: 8192, MaxNodes: 500, MaxCollectionElements: 500)); }
                catch (CodecException error) { Require(error.Offset >= 0 && error.Offset <= malformed.Length, "bounded mutation reports exact source boundary"); }
            }
        }
    }
    private static NbtValue Tree(Random random, int depth) => random.Next(depth >= 3 ? 10 : 13) switch
    {
        0 => new NbtByte((sbyte)random.Next(-128, 128)),
        1 => new NbtShort((short)random.Next(short.MinValue, short.MaxValue)),
        2 => new NbtInt(random.Next()),
        3 => new NbtLong(random.NextInt64()),
        4 => new NbtFloat(random.Next()),
        5 => new NbtDouble(random.NextInt64()),
        6 => new NbtString("值\0\ud800😀" + random.Next()),
        7 => new NbtByteArray(Enumerable.Range(0, random.Next(8)).Select(_ => (sbyte)random.Next(-128, 128))),
        8 => new NbtIntArray(Enumerable.Range(0, random.Next(8)).Select(_ => random.Next())),
        9 => new NbtLongArray(Enumerable.Range(0, random.Next(8)).Select(_ => random.NextInt64())),
        10 => new NbtCompound(Enumerable.Range(0, random.Next(4)).Select(index => new NbtEntry(index == 0 ? "" : "key" + index, Tree(random, depth + 1)))),
        11 => new NbtList(NbtType.Int, Enumerable.Range(0, random.Next(4)).Select(_ => new NbtInt(random.Next()))),
        _ => new NbtList(NbtType.Compound, Enumerable.Range(0, random.Next(4)).Select(_ => new NbtCompound([new("k", Tree(random, depth + 1))])))
    };
}
