using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.SnbtTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtBatchEditsTests
{
    public static void OneSnapshotPreservesFragmentAndProtectedBytes()
    {
        const string input = "say 雪 run { a : 'One', b : \"Two\", c : [I;1,2], keep:'same', n:1.0d } then {a:'One'}";
        foreach (var profile in Enum.GetValues<SnbtProfile>())
        {
            var source = Bytes(input); var start = Bytes(input[..input.IndexOf('{')]).Length; var document = Snbt.ParseFragment(source, start, profile); source[0] = (byte)'X';
            (TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)[] edits = [
                (Key("a"), Leaf(new NbtString("One")), Leaf(new NbtString("Three"))), (Key("b"), Leaf(new NbtString("Two")), Leaf(new NbtString("Four"))),
                (new([PathSegment.ForKey("c"), PathSegment.ForIndex(1)]), Leaf(new NbtInt(2)), Leaf(new NbtInt(3))), (Key("keep"), Leaf(new NbtString("same")), Leaf(new NbtString("same")))];
            var output = Snbt.ReplaceMany(document, edits); var reversed = Snbt.ReplaceMany(document, edits.Reverse());
            var expected = input.Replace("'One', b", "\"Three\", b", StringComparison.Ordinal).Replace("\"Two\"", "\"Four\"", StringComparison.Ordinal).Replace("[I;1,2]", "[I;1,3]", StringComparison.Ordinal);
            Require(output.SequenceEqual(Bytes(expected)) && output.SequenceEqual(reversed) && document.Source.AsSpan().SequenceEqual(Bytes(input)), "batch applies one immutable snapshot, keeps original fragment framing/typed-array syntax/protected bytes and does not depend on request order");
            Require(Snbt.ParseFragment(output, start, profile).Resolve(Key("c")).Value == Leaf(new NbtIntArray([1, 3])), "one readback matches the authorized complete typed result");
            Require(Snbt.ReplaceMany(document, [(Key("keep"), Leaf(new NbtString("same")), Leaf(new NbtString("same")))]).SequenceEqual(Bytes(input)), "no-op slots keep original quote spelling and the whole source snapshot");
        }
        var bounded = Snbt.ParseDocument(Bytes("{a:'old',b:'" + new string('B', 70) + "',n:1.0d}"), limits: new(MaxDecodedBytes: 100));
        (TypedPath Path, LogicalMinecraftValue Expected, LogicalMinecraftValue Replacement)[] pair = [(Key("a"), Leaf(new NbtString("old")), Leaf(new NbtString(new string('A', 70)))), (Key("b"), Leaf(new NbtString(new string('B', 70))), Leaf(new NbtString("tiny")))];
        Require(Snbt.ReplaceMany(bounded, pair).SequenceEqual(Snbt.ReplaceMany(bounded, pair.Reverse())), "batch capacity follows its final complete candidate, independent of expansion/shrink request order");
    }
    public static void RejectedBatchesNeverReturnPartialChanges()
    {
        var document = Snbt.ParseDocument(Bytes("{a:{text:'One'},b:[I;1,2]}")); var path = new TypedPath([PathSegment.ForKey("a"), PathSegment.ForKey("text")]);
        var one = Leaf(new NbtString("One")); var two = Leaf(new NbtString("Two"));
        Reject(SnbtError.Precondition, () => Snbt.ReplaceMany(document, [(path, one, two), (Key("b"), Leaf(new NbtIntArray([1, 99])), Leaf(new NbtIntArray([1, 3])))]));
        Reject(SnbtError.Precondition, () => Snbt.ReplaceMany(document, [(path, one, two), (path, one, two)]));
        var parent = document.Resolve(Key("a")).Value;
        Reject(SnbtError.Precondition, () => Snbt.ReplaceMany(document, [(Key("a"), parent, parent), (path, one, two)]));
        Reject(SnbtError.InvalidArrayElement, () => Snbt.ReplaceMany(document, [(new([PathSegment.ForKey("b"), PathSegment.ForIndex(0)]), Leaf(new NbtInt(1)), two)]));
        var boundary = Snbt.ParseFragment(Bytes("'x'b"), 0);
        Reject(SnbtError.Precondition, () => Snbt.ReplaceMany(boundary, [(TypedPath.Root, Leaf(new NbtString("x")), Leaf(new NbtInt(1)))]));
        var bounded = Snbt.ParseDocument(Bytes("{text:'One'}"), limits: new(MaxDecodedBytes: 100));
        Reject(SnbtError.LimitExceeded, () => Snbt.ReplaceMany(bounded, [(Key("text"), one, Leaf(new NbtString(new string('X', 500))))]));
        CandidateIoTests.Reject<ArgumentException>(() => Snbt.ReplaceMany(document, Enumerable.Repeat((path, (LogicalMinecraftValue)one, (LogicalMinecraftValue)two), 100_001)));
        Require(document.Source.AsSpan().SequenceEqual(Bytes("{a:{text:'One'},b:[I;1,2]}")), "failed batch never changes the retained immutable source or returns a partial candidate");
    }
}
