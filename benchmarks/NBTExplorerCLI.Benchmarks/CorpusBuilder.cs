using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NBTExplorerCLI.Benchmarks;

public sealed record CorpusSummary(string Size, int Files, long Bytes, int Slots, int Chunks, string ManifestSha256);

public static class CorpusBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] Locales = ["en_us", "zh_tw", "de_de", "fr_fr", "es_es", "ja_jp", "ko_kr", "ru_ru"];
    private sealed record CorpusFile(string Path, long Bytes, string Sha256, int Slots, string Profile);

    public static CorpusSummary Generate(string size, string outputRoot)
    {
        var (packs, locales, entries) = size switch
        {
            "S" => (2, 1, 128),
            "M" => (32, 8, 128),
            "L" => (64, 8, 1024),
            _ => throw new ArgumentException("Corpus size 必須是 S、M 或 L", nameof(size))
        };
        var root = Path.GetFullPath(outputRoot);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Corpus 輸出根已存在，拒絕覆寫");
        Directory.CreateDirectory(root);
        var files = new List<CorpusFile>();
        foreach (var relative in new[] { "region/r.0.0.mca", "DIM-1/region/r.-1.0.mca" })
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[8192]);
            files.Add(Describe(path, relative, 0, "anvil-empty-header"));
        }
        for (var pack = 0; pack < packs; pack++)
        {
            var packId = pack.ToString("D3", CultureInfo.InvariantCulture);
            var prefix = $"resourcepacks/pack-{packId}";
            var metadata = Path.Combine(root, prefix, "pack.mcmeta");
            Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
            File.WriteAllBytes(metadata, JsonSerializer.SerializeToUtf8Bytes(new
            {
                pack = new { pack_format = 34, description = $"Fixture pack {packId}" }
            }));
            files.Add(Describe(metadata, prefix + "/pack.mcmeta", 0, "java-1.21-resource-pack-34"));
            for (var localeIndex = 0; localeIndex < locales; localeIndex++)
            {
                var locale = Locales[localeIndex];
                var relative = prefix + $"/assets/fixture/lang/{locale}.json";
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    for (var entry = 0; entry < entries; entry++)
                    {
                        var index = entry.ToString("D6", CultureInfo.InvariantCulture);
                        writer.WriteString("fixture.line." + index, $"Fixture {packId}/{locale}/{index} ‖§‖");
                    }
                    writer.WriteEndObject();
                }
                files.Add(Describe(path, relative, entries, "lang-values-v1"));
            }
        }
        var bytes = files.Sum(file => file.Bytes);
        var slots = files.Sum(file => file.Slots);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaFamily = "nbtcli.corpus-manifest", schemaVersion = 1, recipeVersion = 1, size,
            source = "Project-authored deterministic literals", license = "Project-authored fixture data",
            scope = "LangKey only; empty regions exercise discovery; not a playable world",
            expected = new { files = files.Count, bytes, slots, chunks = 0 },
            files = files.OrderBy(file => file.Path, StringComparer.Ordinal)
        }, JsonOptions);
        File.WriteAllBytes(Path.Combine(root, "manifest.json"), manifest);
        return new(size, files.Count, bytes, slots, 0, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant());
    }

    private static CorpusFile Describe(string path, string relative, int slots, string profile)
    {
        using var stream = File.OpenRead(path);
        return new(relative, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), slots, profile);
    }
}
