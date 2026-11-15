using System.Security.Cryptography;
using System.Text.Json;

namespace Next.Verification;

internal static class CorpusTests
{
    public static void Manifest(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var relative = row.GetProperty("path").GetString()!;
            if (!expected.Add(relative)) throw new InvalidDataException($"重複 fixture：{relative}");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidDataException($"Fixture 超出根目錄：{relative}");
            using var stream = File.OpenRead(path);
            if (stream.Length != row.GetProperty("bytes").GetInt64()
                || !Convert.ToHexString(SHA256.HashData(stream)).Equals(row.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Fixture 大小或 hash 不符：{relative}");
        }
        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => path != "manifest.json").ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual)) throw new InvalidDataException("Fixture manifest 與完整檔案集合不符");
    }

    public static void Cases(string root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var findings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "cases"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (!ids.Add(item.GetProperty("id").GetString()!)) throw new InvalidDataException("Case ID 重複");
                foreach (var finding in item.GetProperty("findings").EnumerateArray()) findings.Add(finding.GetString()!);
            }
        }
        foreach (var finding in Enumerable.Range(1, 20).Select(index => $"F{index:D2}"))
            if (!findings.Contains(finding)) throw new InvalidDataException($"缺少 {finding} 對應案例");
    }
}
