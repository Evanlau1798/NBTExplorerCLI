using Next.Benchmarks;
using System.Text.Json;

namespace Next.Verification;

internal static class CorpusBuilderTests
{
    public static void SmallContract()
    {
        var root = Path.Combine(ScratchDirectory.CreateChild(), "corpus");
        var result = CorpusBuilder.Generate("S", root);
        Require(result.Files == 6 && result.Slots == 256 && result.Chunks == 0, "S corpus 必須具 6 來源、256 lang slots、0 chunks");
        var langFiles = Directory.GetFiles(root, "en_us.json", SearchOption.AllDirectories);
        Require(langFiles.Length == 2, "S 必須有兩個來源包，不能合併同名 entry");
        foreach (var file in langFiles)
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
            Require(json.RootElement.EnumerateObject().Count() == 128, "每包必須保留 128 個可定位的值");
        }
        using var first = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "resourcepacks/pack-000/assets/fixture/lang/en_us.json")));
        using var second = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "resourcepacks/pack-001/assets/fixture/lang/en_us.json")));
        Require(first.RootElement.GetProperty("fixture.line.000000").GetString() == "Fixture 000/en_us/000000 ‖§‖"
            && second.RootElement.GetProperty("fixture.line.000127").GetString() == "Fixture 001/en_us/000127 ‖§‖",
            "來源包、key、原文及 literal marker 必須符合獨立 recipe");
    }

    public static void Deterministic()
    {
        var root = ScratchDirectory.CreateChild();
        var first = CorpusBuilder.Generate("S", Path.Combine(root, "one"));
        var second = CorpusBuilder.Generate("S", Path.Combine(root, "two"));
        Require(first.ManifestSha256.Length == 64 && first.ManifestSha256 == second.ManifestSha256,
            "不同輸出根的 corpus manifest hash 必須相同");
    }

    public static void ProtectsExistingOutput()
    {
        var root = ScratchDirectory.CreateChild();
        var marker = Path.Combine(root, "marker.txt");
        File.WriteAllText(marker, "original");
        try { CorpusBuilder.Generate("S", root); }
        catch (IOException)
        {
            Require(File.ReadAllText(marker) == "original" && Directory.GetFiles(root).Length == 1,
                "既有輸出內容不可被覆蓋或新增檔案");
            return;
        }
        throw new InvalidOperationException("既有輸出根必須拒絕，不能覆寫");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
