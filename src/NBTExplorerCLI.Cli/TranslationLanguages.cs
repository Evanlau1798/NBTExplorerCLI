using System.Runtime.InteropServices;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Cli;

internal static class TranslationLanguages
{
    internal const string Undetermined = "und";
    internal const string Automatic = "auto";

    internal static string SourceOption(string? value)
        => value is null || value.Equals(Automatic, StringComparison.OrdinalIgnoreCase) ? Automatic
            : value.Equals(Undetermined, StringComparison.OrdinalIgnoreCase) ? Undetermined
            : Concrete(value) ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply a valid --source-language tag");

    internal static string Target(string? value)
    {
        if (value is not null)
            return Concrete(value) ?? throw new CliFailure("INVALID_ARGUMENTS", "Supply a concrete --target-language tag");
        return DisplayLanguage() ?? throw new CliFailure("INVALID_ARGUMENTS",
            "No usable system display language. Supply --target-language <language>.");
    }

    internal static string? DisplayLanguage()
    {
        const uint languageNames = 8; // MUI_LANGUAGE_NAME
        uint size = 0;
        if (!OperatingSystem.IsWindows() || !GetUserPreferredUILanguages(languageNames, out _, null, ref size)
            || size is < 2 or > 32768) return null;
        var languages = new char[size];
        if (!GetUserPreferredUILanguages(languageNames, out _, languages, ref size)) return null;
        var end = Array.IndexOf(languages, '\0');
        return end > 0 ? Concrete(new string(languages, 0, end)) : null;
    }

    internal static string? ResourceSource(ArtifactAddress artifact)
    {
        var path = artifact.ContainerEntry ?? artifact.RelativePath;
        var parts = path.Split('/');
        if (parts.Length < 4 || parts[^4] != "assets" || parts[^2] != "lang"
            || !parts[^1].EndsWith(".json", StringComparison.Ordinal)) return null;
        return Concrete(parts[^1][..^5]);
    }

    internal static string? Concrete(string value)
        => Normalize(value) is { } tag && tag.Split('_')[0] is not (Automatic or Undetermined) ? tag : null;

    private static string? Normalize(string value)
    {
        if (value.Length is < 2 or > 128) return null;
        var normalized = value.Replace('-', '_').ToLowerInvariant();
        var parts = normalized.Split('_');
        if (parts[0].Length is < 2 or > 8 || !parts[0].All(char.IsAsciiLetterLower)
            || parts.Skip(1).Any(part => part.Length is < 1 or > 8 || !part.All(char.IsAsciiLetterOrDigit))) return null;
        return normalized;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetUserPreferredUILanguages(uint flags, out uint languageCount,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[]? languages, ref uint size);
}
