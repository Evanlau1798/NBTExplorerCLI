using System.Collections.Immutable;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldDiscoveryFailureTests
{
    private sealed class ExtraInventory(IWorldInventory actual) : IWorldInventory
    {
        public Guid WorldId => actual.WorldId;
        public byte[] Read(ArtifactAddress artifact) => actual.Read(artifact);
        public WorldInventorySnapshot Enumerate(int maxEntries = 100_000)
            => new(actual.Enumerate(maxEntries).Entries.Add(new("invalid?.dat", 0, false, SourceOutcome.Success)), directoryPaths: ["datapacks/invalid?"]);
    }
    public static void MetadataFailuresKeepInventory()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "data/also.dat", [1, 2]);
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(9999, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            var inventory = new WorldInventory(root, World); var unknown = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(!unknown.Complete && unknown.Sources.Any(source => source.Location.Artifact.RelativePath == "data/also.dat" && source.Outcome == SourceOutcome.Unsupported), "metadata failure retains each unprocessed source, not only level.dat");
            Level(root, true); Put(root, "data/minecraft/world_gen_settings.dat", [1, 2]);
            var corrupt = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(!corrupt.Complete && corrupt.Sources.Any(source => source.Location.Artifact.RelativePath == "data/minecraft/world_gen_settings.dat" && source.Outcome == SourceOutcome.Failed)
                && corrupt.Sources.All(source => source.Location.Artifact.RelativePath != "level.dat" || source.Outcome != SourceOutcome.Failed), "decode failure belongs to actual registry artifact, never an invented level.dat failure");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void UnsafeInventoryNameIsExplicit()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "notes.txt", [1]);
            var result = WorldDiscovery.Discover(new ExtraInventory(new WorldInventory(root, World)), new(SnapshotScope.All));
            Require(!result.Complete && result.DiagnosticCode == "InvalidArtifactPath" && result.Sources.Any(source => source.Location.Artifact.RelativePath == "level.dat"), "unaddressable input does not throw or erase valid observations");
            Require(result.Sources.Single(source => source.Location.Artifact.RelativePath == "notes.txt").ContentHash == Hash256.Compute(new byte[] { 1 }), "irrelevant artifact retains raw hash for verified source manifest");
        }
        finally { Directory.Delete(root, true); }
    }
}
