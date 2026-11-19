using System.Text.RegularExpressions;

namespace NBTExplorerCLI.Domain;

internal static partial class VisibleTextProtection
{
    // .NET regex categories match UTF-16 code units, so supplementary private-use planes need surrogate pairs.
    private const string PrivateUseGlyph = @"\p{Co}|[\uDB80-\uDBBE\uDBC0-\uDBFE][\uDC00-\uDFFF]|[\uDBBF\uDBFF][\uDC00-\uDFFD]";
    private const string HexColor = @"[xX](?:§[0-9a-fA-F]){6}";
    private const string NumericTokens = @"(?:[+\-−±＋－][ \t\u00a0\u202f]*)?\p{Nd}+(?:[.,．，]\p{Nd}+)*(?:[eE][+\-−＋－]?\p{Nd}+)?(?:[ \t\u00a0\u202f]*[%％])?|%(?:[1-9][0-9]*\$)?[0-9.]*[sdf]|%%|[%％]";

    // A numeric percentage is one token so the sign and percent symbol cannot migrate to another value.
    [GeneratedRegex(@"§(?:" + HexColor + @"|[^\r\n])?|" + NumericTokens, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NumberAndFormatTokens();

    [GeneratedRegex(@"§(?:" + HexColor + @"|[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\r\n])?|" + NumericTokens + "|" + PrivateUseGlyph,
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NumberFormatAndGlyphTokens();

    internal static bool Equivalent(string source, string target, string role, bool protectPrivateUseGlyphs)
    {
        if (source == target) return true;
        // Indexed argument reordering belongs to the existing component and language format validators.
        var formatted = role == "language.value" || role.EndsWith(".fallback", StringComparison.Ordinal);
        var tokens = protectPrivateUseGlyphs ? NumberFormatAndGlyphTokens() : NumberAndFormatTokens();
        var right = tokens.EnumerateMatches(target);
        foreach (var match in tokens.EnumerateMatches(source))
        {
            var token = source.AsSpan(match.Index, match.Length);
            if (Argument(token, formatted)) continue;
            ReadOnlySpan<char> other;
            do
            {
                if (!right.MoveNext()) return false;
                other = target.AsSpan(right.Current.Index, right.Current.Length);
            } while (Argument(other, formatted));
            if (!token.SequenceEqual(other)) return false;
        }
        while (right.MoveNext())
            if (!Argument(target.AsSpan(right.Current.Index, right.Current.Length), formatted)) return false;
        return true;
    }

    private static bool Argument(ReadOnlySpan<char> token, bool formatted) => formatted && token[0] == '%' && token[^1] is 's' or 'd' or 'f';
}
