using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class WorldInventoryTests
{
    public static void ExactReadOnlyInventoryAndLimit()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            foreach (var path in new[] { "level.dat", "region/r.-1.0.mca", "dimensions/test/path/entities/r.1.1.mca", "datapacks/p/data/models/function/a.mcfunction" })
            {
                var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, [1, 2, 3]);
            }
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
            var source = new WorldInventory(root, World); var result = source.Enumerate();
            Require(result.Complete && result.Entries.Select(entry => entry.RelativePath).SequenceEqual(files)
                && result.Entries.All(entry => !entry.IsDirectory && entry.Bytes == 3 && entry.Outcome == SourceOutcome.Success), "bounded physical inventory preserves every literal file path in canonical order");
            Require(source.Read(new(World, "level.dat", ArtifactKind.Nbt)).SequenceEqual(new byte[] { 1, 2, 3 }), "inventory reads through guarded bounded artifact port");
            Require(!File.Exists(Path.Combine(root, ".nbtcli-writer.lock")) && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == files.Length,
                "read-only enumeration and reading never initialize world/tool files");
            var limited = source.Enumerate(maxEntries: 2);
            Require(!limited.Complete && limited.DiagnosticCode == "CatalogLimitExceeded" && limited.Entries.Length <= 2, "limited inventory is explicit incomplete, retaining bounded observations");
            var large = Path.Combine(root, "oversized.dat"); using (var stream = File.Create(large)) stream.SetLength(67_108_865);
            var oversized = source.Enumerate().Entries.Single(entry => entry.RelativePath == "oversized.dat");
            Require(oversized.Outcome == SourceOutcome.Failed && oversized.DiagnosticCode == "ArtifactSizeLimit", "oversized source is diagnosed before payload allocation");
            CandidateIoTests.Reject<IOException>(() => source.Read(new(World, "oversized.dat", ArtifactKind.Nbt)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public static void ReparseAndRootFailuresAreVisible()
    {
        var root = ScratchDirectory.CreateChild(); var outside = ScratchDirectory.CreateChild(); var link = Path.Combine(root, "link");
        try
        {
            File.WriteAllBytes(Path.Combine(root, "level.dat"), [1]); File.WriteAllBytes(Path.Combine(outside, "secret.dat"), [9]);
            CandidatePathTests.CreateJunction(link, outside);
            var source = new WorldInventory(root, World); var result = source.Enumerate();
            Require(!result.Complete && result.Entries.Any(entry => entry.RelativePath == "level.dat" && entry.Outcome == SourceOutcome.Success)
                && result.Entries.Any(entry => entry.RelativePath == "link" && entry.IsDirectory && entry.Outcome == SourceOutcome.Unsupported && entry.DiagnosticCode == "ReparsePoint")
                && result.Entries.All(entry => !entry.RelativePath.Contains("secret", StringComparison.Ordinal)), "a blocked link is diagnosed alongside good sources and is never traversed");
            CandidateIoTests.Reject<IOException>(() => source.Read(new(World, "link/secret.dat", ArtifactKind.Nbt)));
            var aliased = new WorldInventory(link, World).Enumerate();
            Require(!aliased.Complete && aliased.DiagnosticCode == "RootEnumerationFailed", "aliased root is not reported as an empty complete catalog");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true); Directory.Delete(outside, recursive: true);
        }
    }
}
