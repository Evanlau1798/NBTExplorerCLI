using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

internal static class SnbtOperations
{
    public static NbtValue Evaluate(string operation, LogicalMinecraftValue argument, long offset)
    {
        if (argument is LogicalMinecraftLeaf leaf)
        {
            if (operation == "bool")
            {
                var narrowed = leaf.Value switch
                {
                    NbtByte n => n.Value, NbtShort n => unchecked((sbyte)n.Value), NbtInt n => unchecked((sbyte)n.Value), NbtLong n => unchecked((sbyte)n.Value),
                    NbtFloat n => Narrow(n.Value), NbtDouble n => Narrow(n.Value), _ => throw Invalid(offset)
                };
                return new NbtByte((sbyte)(narrowed != 0 ? 1 : 0));
            }
            if (operation == "uuid" && leaf.Value is NbtString text) return Uuid(text.Value, offset);
        }
        throw Invalid(offset);
    }
    private static NbtIntArray Uuid(string text, long offset)
    {
        var parts = text.Split('-');
        if (text.Length > 36 || parts.Length != 5) throw Invalid(offset);
        var values = new ulong[5];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index].AsSpan();
            if (!part.IsEmpty && part[0] == '+') part = part[1..];
            if (part.IsEmpty) throw Invalid(offset);
            foreach (var unit in part)
            {
                var digit = HexDigit(unit);
                if (digit < 0 || values[index] > ((ulong)long.MaxValue - (uint)digit) / 16) throw Invalid(offset);
                values[index] = values[index] * 16 + (uint)digit;
            }
        }
        var a = values[0] & 0xffffffff; var b = values[1] & 0xffff; var c = values[2] & 0xffff;
        var d = values[3] & 0xffff; var e = values[4] & 0xffffffffffff;
        return new([unchecked((int)a), unchecked((int)((b << 16) | c)), unchecked((int)((d << 16) | (e >> 32))), unchecked((int)e)]);
    }
    private static sbyte Narrow(double value)
    {
        var integer = double.IsNaN(value) ? 0 : value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
        return unchecked((sbyte)integer);
    }
    private static int HexDigit(char unit)
    {
        if (unit is >= 'A' and <= 'F') return unit - 'A' + 10;
        if (unit is >= 'a' and <= 'f') return unit - 'a' + 10;
        if (unit is >= '\uff21' and <= '\uff26') return unit - '\uff21' + 10;
        if (unit is >= '\uff41' and <= '\uff46') return unit - '\uff41' + 10;
        return char.GetUnicodeCategory(unit) == System.Globalization.UnicodeCategory.DecimalDigitNumber
            ? System.Globalization.CharUnicodeInfo.GetDecimalDigitValue(unit) : -1;
    }
    private static SnbtException Invalid(long offset) => new(SnbtError.InvalidToken, offset, "Unknown or invalid SNBT operation/argument");
}
