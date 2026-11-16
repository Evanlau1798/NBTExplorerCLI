using System.Text;

namespace Next.Domain;

public static class PercentPlaceholders
{
    public static bool Equivalent(string source, string target, bool normalizeNumbers = false)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target);
        var left = Arguments(source, normalizeNumbers) ?? []; var right = Arguments(target, normalizeNumbers) ?? [];
        return left.Count == right.Count && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);
    }
    private static Dictionary<int, int>? Arguments(string text, bool normalizeNumbers)
    {
        if (text.Length > 33_554_432) throw new PlaceholderLimitException();
        if (normalizeNumbers) text = Normalize(text);
        var arguments = new Dictionary<int, int>(); var implicitIndex = 1; var count = 0;
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] != '%') continue;
            var next = position + 1;
            if (next == text.Length) return null;
            if (text[next] == '%') { position = next; continue; }
            var index = implicitIndex; var start = next; long number = 0; var overflow = false;
            while (next < text.Length && char.IsAsciiDigit(text[next]))
            {
                if (number > (int.MaxValue - (text[next] - '0')) / 10L) overflow = true;
                if (!overflow) number = number * 10 + text[next] - '0';
                next++;
            }
            if (next != start)
            {
                if (overflow || number == 0 || next == text.Length || text[next++] != '$') return null;
                index = (int)number;
            }
            else implicitIndex++;
            if (next == text.Length || text[next] != 's') return null;
            if (++count > 100_000) throw new PlaceholderLimitException();
            arguments[index] = arguments.GetValueOrDefault(index) + 1; position = next;
        }
        return arguments;
    }
    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] != '%') { result.Append(text[position]); continue; }
            var next = position + 1; var digits = next;
            while (next < text.Length && char.IsAsciiDigit(text[next])) next++;
            var indexed = next > digits && next < text.Length && text[next] == '$';
            var indexEnd = indexed ? next + 1 : digits; next = indexEnd;
            while (next < text.Length && (char.IsAsciiDigit(text[next]) || text[next] == '.')) next++;
            if (next < text.Length && text[next] is 'd' or 'f')
            {
                result.Append('%'); if (indexed) result.Append(text.AsSpan(digits, indexEnd - digits)); result.Append('s'); position = next;
            }
            else result.Append('%');
        }
        return result.ToString();
    }
}

public sealed class PlaceholderLimitException() : Exception("Placeholder contract exceeds the text or occurrence budget");
