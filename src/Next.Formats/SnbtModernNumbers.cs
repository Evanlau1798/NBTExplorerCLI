using Next.Domain;
using System.Globalization;

namespace Next.Formats;

internal static class SnbtModernNumbers
{
    public static bool Starts(char unit) => char.IsAsciiDigit(unit) || unit is '.' or '+' or '-';
    public static NbtValue Parse(string token, NbtType context, long offset)
    {
        var raw = token.AsSpan();
        var negative = raw[0] == '-';
        if (raw[0] is '-' or '+') raw = raw[1..];
        if (raw.IsEmpty) throw Invalid(offset);
        var radix = 10;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { radix = 16; raw = raw[2..]; }
        else if (raw.Length > 2 && raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { radix = 2; raw = raw[2..]; }
        if (raw.IsEmpty) throw Invalid(offset);
        var type = context == NbtType.End ? NbtType.Int : context;
        var signed = radix == 10;
        var suffix = char.ToLowerInvariant(raw[^1]);
        if (radix == 10 && (suffix is 'f' or 'd' || raw.ContainsAny('.', 'e', 'E')))
        {
            if (suffix is 'f' or 'd') raw = raw[..^1];
            if (!FloatSyntax(raw)) throw Invalid(offset);
            var clean = Clean(raw);
            if (negative) clean = "-" + clean;
            if (suffix == 'f')
            {
                if (float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number)) return SnbtLiterals.Float(number);
            }
            else if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) return SnbtLiterals.Double(number);
            throw Invalid(offset);
        }
        var explicitSign = raw.Length > 1 && char.ToLowerInvariant(raw[^2]) is 's' or 'u';
        if (suffix is 'b' or 's' or 'i' or 'l' && (radix != 16 || suffix != 'b' || explicitSign))
        {
            type = suffix switch { 'b' => NbtType.Byte, 's' => NbtType.Short, 'i' => NbtType.Int, _ => NbtType.Long };
            if (explicitSign) { signed = char.ToLowerInvariant(raw[^2]) == 's'; raw = raw[..^2]; }
            else raw = raw[..^1];
        }
        if (!Digits(raw, radix) || radix == 10 && Clean(raw) is { Length: > 1 } decimalText && decimalText[0] == '0') throw Invalid(offset);
        ulong value = 0;
        foreach (var unit in raw)
        {
            if (unit == '_') continue;
            var digit = (uint)Digit(unit);
            if (value > (ulong.MaxValue - digit) / (uint)radix) throw Invalid(offset);
            value = value * (uint)radix + digit;
        }
        var width = type switch { NbtType.Byte => 8, NbtType.Short => 16, NbtType.Int => 32, _ => 64 };
        var signBit = 1UL << (width - 1);
        var maximum = signed ? negative ? signBit : signBit - 1 : width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        if (value > maximum || negative && !signed) throw Invalid(offset);
        var bits = negative ? unchecked(0UL - value) : value;
        return type switch
        {
            NbtType.Byte => new NbtByte(unchecked((sbyte)bits)), NbtType.Short => new NbtShort(unchecked((short)bits)),
            NbtType.Int => new NbtInt(unchecked((int)bits)), _ => new NbtLong(unchecked((long)bits))
        };
    }
    private static int Digit(char unit) => unit is >= '0' and <= '9' ? unit - '0' : unit is >= 'a' and <= 'f' ? unit - 'a' + 10 : unit is >= 'A' and <= 'F' ? unit - 'A' + 10 : -1;
    private static bool Digits(ReadOnlySpan<char> text, int radix)
    {
        if (text.IsEmpty || text[0] == '_' || text[^1] == '_') return false;
        foreach (var unit in text) if (unit != '_' && (Digit(unit) < 0 || Digit(unit) >= radix)) return false;
        return true;
    }
    private static string Clean(ReadOnlySpan<char> text) => text.ToString().Replace("_", "", StringComparison.Ordinal);
    private static bool FloatSyntax(ReadOnlySpan<char> text)
    {
        var exponent = text.IndexOfAny('e', 'E');
        if (exponent >= 0)
        {
            var power = text[(exponent + 1)..];
            if (!power.IsEmpty && power[0] is '+' or '-') power = power[1..];
            if (!Digits(power, 10)) return false;
            text = text[..exponent];
        }
        var dot = text.IndexOf('.');
        if (dot < 0) return Digits(text, 10);
        var whole = text[..dot]; var fraction = text[(dot + 1)..];
        return (!whole.IsEmpty || !fraction.IsEmpty) && (whole.IsEmpty || Digits(whole, 10)) && (fraction.IsEmpty || Digits(fraction, 10));
    }
    private static SnbtException Invalid(long offset) => new(SnbtError.InvalidNumber, offset, "Invalid or out-of-range modern SNBT number");
}
