using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Next.Formats;

internal static class SnbtUnicodeNames
{
    private static readonly Lazy<Dictionary<string, int>> Names = new(Load);
    public static bool TryFind(string name, out int point) => Names.Value.TryGetValue(name.Trim(' '), out point);
    private static Dictionary<string, int> Load()
    {
        using var resource = typeof(SnbtUnicodeNames).Assembly.GetManifestResourceStream("Next.Formats.Unicode.java21-names.json")
            ?? throw new InvalidDataException("Missing Unicode name data");
        using var json = JsonDocument.Parse(resource);
        using var compressed = new MemoryStream(json.RootElement.GetProperty("gzipBase64").GetBytesFromBase64());
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var names = new Dictionary<string, int>(json.RootElement.GetProperty("entries").GetInt32(), StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf('\t');
            names.Add(line[(separator + 1)..], int.Parse(line.AsSpan(0, separator), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        if (names.Count != json.RootElement.GetProperty("entries").GetInt32()) throw new InvalidDataException("Incomplete Unicode name data");
        return names;
    }
}
