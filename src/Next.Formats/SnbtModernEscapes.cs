namespace Next.Formats;

internal static class SnbtModernEscapes
{
    public static string Read(string source, ref int position)
    {
        var unit = source[position++];
        if (unit == 'N')
        {
            if (position == source.Length || source[position++] != '{') throw Invalid();
            var start = position;
            while (position < source.Length && source[position] != '}')
            {
                var current = source[position++];
                if (!(char.IsAsciiLetterOrDigit(current) || current is ' ' or '-') || position - start > 65_535) throw Invalid();
            }
            if (position == source.Length) throw Invalid();
            var name = source[start..position++];
            if (!SnbtUnicodeNames.TryFind(name, out var point)) throw Invalid();
            return CodePoint((uint)point);
        }
        if (unit is 'x' or 'u' or 'U')
        {
            var count = unit == 'x' ? 2 : unit == 'u' ? 4 : 8;
            uint point = 0;
            for (var index = 0; index < count; index++)
            {
                if (position == source.Length) throw Invalid();
                var digit = source[position++];
                var value = digit is >= '0' and <= '9' ? digit - '0' : digit is >= 'a' and <= 'f' ? digit - 'a' + 10 : digit is >= 'A' and <= 'F' ? digit - 'A' + 10 : -1;
                if (value < 0) throw Invalid();
                point = (point << 4) | (uint)value;
            }
            return CodePoint(point);
        }
        return unit switch
        {
            '\'' => "'", '"' => "\"", '\\' => "\\", 'b' => "\b", 's' => " ", 't' => "\t", 'n' => "\n", 'f' => "\f", 'r' => "\r",
            _ => throw Invalid()
        };
    }
    private static string CodePoint(uint point)
    {
        if (point > 0x10ffff) throw Invalid();
        return point <= 0xffff ? ((char)point).ToString() : char.ConvertFromUtf32((int)point);
    }
    private static FormatException Invalid() => new("Invalid modern SNBT escape");
}
