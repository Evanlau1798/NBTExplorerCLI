using System.Text.RegularExpressions;

namespace NBTExplorerCLI.Domain;

internal static partial class VisibleTextProtection
{
    // A numeric percentage is one token so the sign and percent symbol cannot migrate to another value.
    [GeneratedRegex(@"§(?:[xX](?:§[0-9a-fA-F]){6}|[^\r\n])?|(?:[+\-−±＋－][ \t\u00a0\u202f]*)?\p{Nd}+(?:[.,．，]\p{Nd}+)*(?:[eE][+\-−＋－]?\p{Nd}+)?(?:[ \t\u00a0\u202f]*[%％])?|%(?:[1-9][0-9]*\$)?[0-9.]*[sdf]|%%|[%％]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Tokens();

    internal static bool Equivalent(string source, string target, string role)
    {
        if (source == target) return true;
        // Indexed argument reordering belongs to the existing component and language format validators.
        var formatted = role == "language.value" || role.EndsWith(".fallback", StringComparison.Ordinal);
        var right = Tokens().EnumerateMatches(target);
        foreach (var match in Tokens().EnumerateMatches(source))
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
