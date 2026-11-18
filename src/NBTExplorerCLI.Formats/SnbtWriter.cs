using NBTExplorerCLI.Domain;
using System.Globalization;
using System.Text;

namespace NBTExplorerCLI.Formats;

internal sealed class SnbtWriter(DecodeLimits limits, SnbtProfile profile)
{
    private readonly StringBuilder _output = new();
    private readonly SnbtBudget _budget = new(limits);
    private long _bytes;
    private void Add(string text)
    {
        try { _bytes = checked(_bytes + Snbt.Utf8.GetByteCount(text)); }
        catch (EncoderFallbackException) { throw new SnbtException(SnbtError.UnsupportedValue, _bytes, "This SNBT profile cannot encode isolated surrogates"); }
        if (_bytes > limits.MaxDecodedBytes) SnbtBudget.Limit(_bytes);
        _output.Append(text);
    }
    private void Text(string text)
    {
        _budget.Text(text, _bytes);
        var quoted = new StringBuilder("\"");
        foreach (var unit in text)
        {
            if (profile == SnbtProfile.Java1215 && (char.IsSurrogate(unit) || char.IsControl(unit)))
            {
                quoted.Append("\\u").Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture)); continue;
            }
            if (unit is '\\' or '"') quoted.Append('\\');
            quoted.Append(unit);
        }
        quoted.Append('"'); Add(quoted.ToString());
    }
    public byte[] Write(LogicalMinecraftValue value)
    {
        Value(value, 0);
        return Snbt.Utf8.GetBytes(_output.ToString());
    }
    private void Value(LogicalMinecraftValue value, int depth)
    {
        _budget.Node(depth, _bytes);
        switch (value)
        {
            case LogicalMinecraftCompound compound:
                Add("{");
                for (var index = 0; index < compound.Entries.Length; index++)
                {
                    _budget.Entry(_bytes); if (index > 0) Add(",");
                    if (compound.Entries[index].Name.Length == 0)
                        throw new SnbtException(SnbtError.UnsupportedValue, _bytes, "Java SNBT cannot encode an empty key");
                    Text(compound.Entries[index].Name); Add(":"); Value(compound.Entries[index].Value, depth + 1);
                }
                Add("}"); break;
            case LogicalMinecraftList list:
                if (list.Items.IsEmpty && list.EmptyElementType != NbtType.End)
                    throw new SnbtException(SnbtError.UnsupportedValue, _bytes, "SNBT cannot represent an empty list's non-End header");
                if (profile == SnbtProfile.JavaThrough1214 && list.Items.Select(SnbtLiterals.Type).Distinct().Skip(1).Any())
                    throw new SnbtException(SnbtError.MixedList, _bytes, "SNBT through Java 1.21.4 requires homogeneous lists");
                Add("[");
                for (var index = 0; index < list.Items.Length; index++)
                {
                    _budget.Element(_bytes); if (index > 0) Add(","); Value(list.Items[index], depth + 1);
                }
                Add("]"); break;
            case LogicalMinecraftLeaf leaf: Leaf(leaf.Value, depth); break;
            default: throw new ArgumentException("Unknown logical value", nameof(value));
        }
    }
    private void Leaf(NbtValue value, int depth)
    {
        var culture = CultureInfo.InvariantCulture;
        switch (value)
        {
            case NbtString text: Text(text.Value); break;
            case NbtByte number: Add(number.Value.ToString(culture) + "b"); break;
            case NbtShort number: Add(number.Value.ToString(culture) + "s"); break;
            case NbtInt number: Add(number.Value.ToString(culture)); break;
            case NbtLong number: Add(number.Value.ToString(culture) + "L"); break;
            case NbtFloat number when float.IsFinite(number.Value) && number.RawBits != int.MinValue: Add(number.Value.ToString("R", culture) + "f"); break;
            case NbtDouble number when double.IsFinite(number.Value) && number.RawBits != long.MinValue: Add(number.Value.ToString("R", culture) + "d"); break;
            case NbtFloat number when profile == SnbtProfile.JavaThrough1214 && float.IsInfinity(number.Value): Add((number.Value < 0 ? "-" : "") + "1e9999f"); break;
            case NbtDouble number when profile == SnbtProfile.JavaThrough1214 && double.IsInfinity(number.Value): Add((number.Value < 0 ? "-" : "") + "1e9999d"); break;
            case NbtByteArray array: Array("B", array.Values.Select(number => (NbtValue)new NbtByte(number)), depth); break;
            case NbtIntArray array: Array("I", array.Values.Select(number => (NbtValue)new NbtInt(number)), depth); break;
            case NbtLongArray array: Array("L", array.Values.Select(number => (NbtValue)new NbtLong(number)), depth); break;
            default: throw new SnbtException(SnbtError.UnsupportedValue, _bytes, "Value has no lossless SNBT representation");
        }
    }
    private void Array(string prefix, IEnumerable<NbtValue> values, int depth)
    {
        Add("[" + prefix + ";"); var index = 0;
        foreach (var value in values)
        {
            _budget.Element(_bytes); _budget.Node(depth + 1, _bytes);
            if (index++ > 0) Add(","); Leaf(value, depth + 1);
        }
        Add("]");
    }
}
