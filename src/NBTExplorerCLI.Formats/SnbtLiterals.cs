using NBTExplorerCLI.Domain;
using System.Globalization;

namespace NBTExplorerCLI.Formats;

internal static class SnbtLiterals
{
    public static bool Unquoted(char value) => char.IsAsciiLetterOrDigit(value) || value is '_' or '-' or '+' or '.';
    public static bool White(char value) => char.IsWhiteSpace(value) && value is not ('\u0085' or '\u00a0' or '\u2007' or '\u202f') || value is >= '\u001c' and <= '\u001f';
    public static NbtValue Classic(string token)
    {
        if (token.Equals("true", StringComparison.OrdinalIgnoreCase)) return new NbtByte(1);
        if (token.Equals("false", StringComparison.OrdinalIgnoreCase)) return new NbtByte(0);
        var body = token.AsSpan(0, token.Length - 1);
        var style = NumberStyles.AllowLeadingSign;
        var culture = CultureInfo.InvariantCulture;
        switch (char.ToLowerInvariant(token[^1]))
        {
            case 'b' when Integer(body) && sbyte.TryParse(body, style, culture, out var number): return new NbtByte(number);
            case 's' when Integer(body) && short.TryParse(body, style, culture, out var number): return new NbtShort(number);
            case 'l' when Integer(body) && long.TryParse(body, style, culture, out var number): return new NbtLong(number);
            case 'f' when Numeric(body) && float.TryParse(body, NumberStyles.Float, culture, out var number): return Float(number);
            case 'd' when Numeric(body) && double.TryParse(body, NumberStyles.Float, culture, out var number): return Double(number);
        }
        if (Integer(token) && int.TryParse(token, style, culture, out var integer)) return new NbtInt(integer);
        if (token.Contains('.') && Numeric(token)
            && double.TryParse(token, NumberStyles.Float, culture, out var real)) return Double(real);
        return new NbtString(token);
    }
    // Official Tag factories share positive zero for both parsed zero signs.
    public static NbtFloat Float(float value) => NbtFloat.FromValue(value == 0 ? 0 : value);
    public static NbtDouble Double(double value) => NbtDouble.FromValue(value == 0 ? 0 : value);
    private static bool Integer(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty && text[0] is '+' or '-') text = text[1..];
        if (text.IsEmpty || text.Length > 1 && text[0] == '0') return false;
        foreach (var unit in text) if (!char.IsAsciiDigit(unit)) return false;
        return true;
    }
    private static bool Numeric(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return false;
        foreach (var unit in text) if (!char.IsAsciiDigit(unit) && unit is not ('+' or '-' or '.' or 'e' or 'E')) return false;
        return true;
    }
    public static NbtType Type(LogicalMinecraftValue value) => value switch
    {
        LogicalMinecraftLeaf leaf => leaf.Value.Type,
        LogicalMinecraftList => NbtType.List,
        _ => NbtType.Compound
    };
}
