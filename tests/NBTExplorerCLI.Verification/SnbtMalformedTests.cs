using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.SnbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtMalformedTests
{
    public static void TruncatedAndSeededMalformed()
    {
        var complete = Bytes("{text:'雪\U00020000',array:[I;-1,2],list:[{keep:3}]} ");
        var rootLength = complete.Length - 1;
        foreach (var profile in Enum.GetValues<SnbtProfile>())
            for (var count = 0; count < rootLength; count++)
            {
                try { Snbt.ParseDocument(complete[..count], profile); }
                catch (SnbtException error) when (error.Offset >= 0 && error.Offset <= count) { continue; }
                throw new InvalidOperationException("Container proper prefix不可成功或洩漏非typed例外");
            }
        var random = new Random(0x5042);
        const string alphabet = "{}[]:;,\\\"'0129abefux_+- \t\n";
        var limits = new DecodeLimits(MaxDecodedBytes: 256, MaxDepth: 16, MaxNodes: 128, MaxContainerEntries: 64, MaxCollectionElements: 64, MaxStringBytes: 128);
        for (var sample = 0; sample < 512; sample++)
        {
            var text = new string(Enumerable.Range(0, random.Next(97)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var source = Bytes(text);
            try { Snbt.ParseDocument(source, sample % 2 == 0 ? SnbtProfile.JavaThrough1214 : SnbtProfile.Java1215, limits); }
            catch (SnbtException error) when (error.Offset >= 0 && error.Offset <= source.Length) { }
        }
    }
    public static void SeededTypedRoundtrips()
    {
        var random = new Random(0x5302);
        foreach (var profile in Enum.GetValues<SnbtProfile>())
            for (var sample = 0; sample < 64; sample++)
            {
                var value = Value(random, 0, profile);
                Require(Snbt.ParseDocument(Snbt.Write(value, profile), profile).Root.Value == value, "固定seed的typed tree須可逆，不能丟bits／array kind／keys");
            }
    }
    private static LogicalMinecraftValue Value(Random random, int depth, SnbtProfile profile)
    {
        switch (random.Next(depth < 4 ? 9 : 6))
        {
            case 0: return Leaf(new NbtInt(random.Next(int.MinValue, int.MaxValue)));
            case 1: return Leaf(new NbtByte((sbyte)random.Next(-128, 128)));
            case 2: return Leaf(new NbtString(profile == SnbtProfile.Java1215 ? "雪\ud800\n\"\U00020000" : "雪\n\"\U00020000"));
            case 3: return Leaf(profile == SnbtProfile.Java181 ? new NbtShort((short)random.Next(short.MinValue, short.MaxValue)) : new NbtIntArray([random.Next(), -1]));
            case 4: return Leaf(new NbtDouble(SampleBits(random)));
            case 5: return Leaf(profile == SnbtProfile.Java181 ? new NbtLong(random.NextInt64()) : new NbtLongArray([long.MinValue, random.NextInt64()]));
            case 6: return new LogicalMinecraftCompound(Enumerable.Range(0, random.Next(4)).Select(index => new LogicalMinecraftEntry("key" + index, Value(random, depth + 1, profile))));
            default: return profile != SnbtProfile.Java1215 ? new LogicalMinecraftList([Leaf(new NbtInt(random.Next())), Leaf(new NbtInt(-1))])
                : new LogicalMinecraftList(Enumerable.Range(0, random.Next(4)).Select(_ => Value(random, depth + 1, profile)));
        }
    }
    private static long SampleBits(Random random)
    {
        var bits = random.NextInt64(long.MinValue, long.MaxValue);
        return double.IsFinite(BitConverter.Int64BitsToDouble(bits)) && bits != long.MinValue ? bits : 0;
    }
}
