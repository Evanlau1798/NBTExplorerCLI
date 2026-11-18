using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldDiscoveryCorpusTests
{
    public static void ReadOnlyGoldenWorlds(string? corpusRoot)
    {
        if (corpusRoot is null) throw new ArgumentException("Specify golden-world parent through --artifact-root for this explicit case");
        var totalChunks = 0; var totalRegions = 0; var totalFiles = 0;
        foreach (var (directory, version) in new[] { ("GoldenTestWorld-1.21", 3953), ("golden_test_world_26.1", 4774) })
        {
            var root = Path.Combine(corpusRoot, directory); var inventory = new WorldInventory(root, World);
            var physical = inventory.Enumerate();
            var before = physical.Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))), StringComparer.Ordinal);
            var result = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(result.Metadata?.DataVersion == version && result.DiagnosticCode is null, $"{directory}: actual known version and complete enumeration");
            var outer = result.Sources.Where(source => source.Location.Artifact.ContainerEntry is null && source.Chunk is null).ToArray();
            Require(outer.Select(source => source.Location.Artifact.RelativePath).Order(StringComparer.Ordinal).SequenceEqual(physical.Entries.Select(entry => entry.RelativePath).Order(StringComparer.Ordinal)), "each real literal artifact has a catalog result");
            foreach (var source in result.Sources.Where(source => source.Outcome is SourceOutcome.Failed or SourceOutcome.Unsupported))
                Require(directory == "GoldenTestWorld-1.21" && source.Location.Role == WorldSourceRole.PointOfInterest
                    && source.Location.Artifact.RelativePath is "poi/r.-1.0.mca" or "poi/r.0.-1.mca" && source.DiagnosticCode == "InvalidRegion", $"unexpected real-source outcome: {source.Location.Artifact.RelativePath}/{source.DiagnosticCode}");
            Require(result.Complete == (version == 4774), "zero-byte POI remains incomplete while snapshot6 catalog completes");
            totalChunks += result.Sources.Count(source => source.Chunk is not null);
            totalRegions += outer.Count(source => source.Location.Role is WorldSourceRole.Terrain or WorldSourceRole.Entities or WorldSourceRole.PointOfInterest);
            totalFiles += physical.Entries.Length;
            Require(before.Count == inventory.Enumerate().Entries.Length && before.All(pair => Hash256.Compute(inventory.Read(new(World, pair.Key, ArtifactKind.Text))) == pair.Value), "read-only discovery preserves full file set and raw bytes");
        }
        Require(totalFiles == 139 && totalRegions == 24 && totalChunks == 7370, $"fixed local corpus catalog: files={totalFiles}, region artifacts={totalRegions}, occupied chunks={totalChunks}");
    }
}
