using System.Text;

namespace NBTExplorerCLI.Domain;

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
            var cursor = position + 1;
            if (cursor == text.Length) return null;
            if (text[cursor] == '%') { position = cursor; continue; }
            var index = implicitIndex; var start = cursor; long number = 0; var overflow = false;
            while (cursor < text.Length && char.IsAsciiDigit(text[cursor]))
            {
                if (number > (int.MaxValue - (text[cursor] - '0')) / 10L) overflow = true;
                if (!overflow) number = number * 10 + text[cursor] - '0';
                cursor++;
            }
            if (cursor != start)
            {
                if (overflow || number == 0 || cursor == text.Length || text[cursor++] != '$') return null;
                index = (int)number;
            }
            else implicitIndex++;
            if (cursor == text.Length || text[cursor] != 's') return null;
            if (++count > 100_000) throw new PlaceholderLimitException();
            arguments[index] = arguments.GetValueOrDefault(index) + 1; position = cursor;
        }
        return arguments;
    }
    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] != '%') { result.Append(text[position]); continue; }
            var cursor = position + 1; var digits = cursor;
            while (cursor < text.Length && char.IsAsciiDigit(text[cursor])) cursor++;
            var indexed = cursor > digits && cursor < text.Length && text[cursor] == '$';
            var indexEnd = indexed ? cursor + 1 : digits; cursor = indexEnd;
            while (cursor < text.Length && (char.IsAsciiDigit(text[cursor]) || text[cursor] == '.')) cursor++;
            if (cursor < text.Length && text[cursor] is 'd' or 'f')
            {
                result.Append('%'); if (indexed) result.Append(text.AsSpan(digits, indexEnd - digits)); result.Append('s'); position = cursor;
            }
            else result.Append('%');
        }
        return result.ToString();
    }
}

public sealed class PlaceholderLimitException() : Exception("Placeholder contract exceeds the text or occurrence budget");
