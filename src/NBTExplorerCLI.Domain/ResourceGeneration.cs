using System.Collections.Immutable;
using System.Text;

namespace NBTExplorerCLI.Domain;

public enum ResourceOutputFormat { LanguageFiles, ZipPack, DirectoryPack }
public sealed record ResourceLanguageEntry(string Key, string Text);
public sealed record ResourceLanguage(string Locale, ImmutableArray<ResourceLanguageEntry> Values);
public sealed record ResourceMergeEntry(string Path, Hash256 Hash, long Bytes);
public sealed record ResourceMerge(Hash256 BlobHash, ImmutableArray<ResourceMergeEntry> Entries);

public sealed class ResourceGeneration
{
    public const int Version = 1;
    public const string Profile = "resource-output-v1";
    public const string Policy = "resource-generation-v1";
    public Guid WorldId { get; }
    public ResourceOutputFormat Format { get; }
    public string Destination { get; }
    public ImmutableArray<ResourceLanguage> Languages { get; }
    public PackFormatVersion? PackFormat { get; }
    public ResourceMerge? Merge { get; }
    public ImmutableArray<ArtifactAddress> Outputs { get; }
    public ResourceGeneration(Guid worldId, ResourceOutputFormat format, string destination, IEnumerable<ResourceLanguage> languages,
        PackFormatVersion? packFormat = null, ResourceMerge? merge = null)
    {
        if (worldId == Guid.Empty) throw new ArgumentException("Resource outputs require a physical scope identity", nameof(worldId));
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(languages);
        if (format == ResourceOutputFormat.LanguageFiles)
        {
            if (destination.Length != 0) Path(destination);
            if (packFormat is not null || merge is not null) throw new ArgumentException("Standalone language files have no pack format or merge");
        }
        else
        {
            Path(destination);
            if (format == ResourceOutputFormat.ZipPack && !destination.EndsWith(".zip", StringComparison.Ordinal)
                || packFormat is null || packFormat.Major < 1) throw new ArgumentException("Pack generation requires one exact relative destination and a positive format");
        }
        long retained = 64 + (long)destination.Length * 2; var entries = 0;
        void Charge(long bytes)
        {
            if (bytes > 67_108_864 - retained) throw new ArgumentException("Resource recipe exceeds its UTF16 retention budget");
            retained += bytes;
        }
        var frozen = ImmutableArray.CreateBuilder<ResourceLanguage>(); var locales = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            ArgumentNullException.ThrowIfNull(language);
            if (frozen.Count == 256 || !Locale(language.Locale) || !locales.Add(language.Locale) || language.Values.IsDefault)
                throw new ArgumentException("Resource languages require distinct bounded resource locales and explicit values");
            Charge(64 + (long)language.Locale.Length * 2);
            var values = ImmutableArray.CreateBuilder<ResourceLanguageEntry>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in language.Values)
            {
                ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(value.Key); ArgumentNullException.ThrowIfNull(value.Text);
                if (++entries > 100_000 || value.Key.Length > 4096 || !keys.Add(value.Key)) throw new ArgumentException("Resource keys must be unique and bounded");
                Charge(64 + ((long)value.Key.Length + value.Text.Length) * 2); values.Add(value);
            }
            frozen.Add(new(language.Locale, values.OrderBy(value => value.Key, StringComparer.Ordinal).ToImmutableArray()));
        }
        if (frozen.Count == 0) throw new ArgumentException("Resource generation requires at least one declared language");
        ResourceMerge? copy = null;
        if (merge is not null)
        {
            ArgumentNullException.ThrowIfNull(merge.BlobHash);
            if (merge.Entries.IsDefault || merge.Entries.Length is < 1 or > 100_000) throw new ArgumentException("Merge requires a bounded exact source manifest");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long decoded = 0;
            foreach (var entry in merge.Entries)
            {
                ArgumentNullException.ThrowIfNull(entry); ArgumentNullException.ThrowIfNull(entry.Hash); Path(entry.Path);
                if (entry.Path.Contains('/') && !entry.Path.StartsWith("assets/", StringComparison.Ordinal)
                    || !paths.Add(entry.Path) || entry.Bytes < 0 || entry.Bytes > 67_108_864 - decoded)
                    throw new ArgumentException("Merge entries require unambiguous resource paths and bounded bytes");
                decoded += entry.Bytes; Charge(96 + (long)entry.Path.Length * 2);
            }
            if (!merge.Entries.Any(entry => entry.Path == "pack.mcmeta")) throw new ArgumentException("Merge source must declare exact pack metadata");
            copy = new(merge.BlobHash, merge.Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToImmutableArray());
        }
        (WorldId, Format, Destination, Languages, PackFormat, Merge) = (worldId, format, destination,
            frozen.OrderBy(language => language.Locale, StringComparer.Ordinal).ToImmutableArray(), packFormat, copy);
        var outputs = new Dictionary<string, ArtifactAddress>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, ArtifactKind kind)
        {
            Path(path);
            if (outputs.TryGetValue(path, out var previous))
            { if (previous.RelativePath != path || previous.Kind != kind) throw new ArgumentException("Resource output aliases another physical path"); return; }
            if (outputs.Count == 100_000) throw new ArgumentException("Resource generation exceeds the physical output budget");
            Charge(96 + (long)path.Length * 2);
            outputs.Add(path, new(worldId, path, kind));
        }
        if (format == ResourceOutputFormat.ZipPack) Add(destination, ArtifactKind.Archive);
        else
        {
            if (format == ResourceOutputFormat.DirectoryPack) Add(destination + "/pack.mcmeta", ArtifactKind.Json);
            foreach (var language in Languages)
                Add(format == ResourceOutputFormat.LanguageFiles ? (destination.Length == 0 ? "" : destination + "/") + language.Locale + ".json" : destination + "/assets/minecraft/lang/" + language.Locale + ".json", ArtifactKind.Json);
            foreach (var entry in Merge?.Entries ?? []) Add(destination + "/" + entry.Path,
                entry.Path == "pack.mcmeta" || entry.Path.EndsWith(".json", StringComparison.Ordinal) ? ArtifactKind.Json : ArtifactKind.Text);
        }
        Outputs = outputs.Values.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray();
    }
    private static bool Locale(string? value) => value is { Length: > 0 and <= 64 } && value is not ("." or "..")
        && value.All(unit => char.IsAsciiLetterLower(unit) || char.IsAsciiDigit(unit) || unit is '_' or '-' or '.');
    private static void Path(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 4096) throw new ArgumentException("Resource path exceeds its length budget");
        ArtifactAddress.ValidatePath(value, false);
        var remaining = value.AsSpan();
        while (true)
        {
            var separator = remaining.IndexOf('/'); var part = separator < 0 ? remaining : remaining[..separator];
            var dot = part.IndexOf('.'); var stem = (dot < 0 ? part : part[..dot]).TrimEnd(' ');
            if (part.Length > 255 || part.StartsWith(".nbtcli-".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CON".AsSpan(), StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX".AsSpan(), StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$".AsSpan(), StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CLOCK$".AsSpan(), StringComparison.OrdinalIgnoreCase)
                || stem.Length == 4 && (stem[..3].Equals("COM".AsSpan(), StringComparison.OrdinalIgnoreCase) || stem[..3].Equals("LPT".AsSpan(), StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]))
                throw new ArgumentException("Resource path contains an unsupported Windows component");
            if (separator < 0) break;
            remaining = remaining[(separator + 1)..];
        }
        _ = new UTF8Encoding(false, true).GetByteCount(value);
    }
}
