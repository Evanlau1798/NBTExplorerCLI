using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldDiscoveryReviewTests
{
    public static void WriterLeaseIsInternal()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); var store = new ArtifactStorage(root, World);
            using (var candidate = store.Stage(new(World, "example.txt", ArtifactKind.Text), ArtifactImage.Absent, new byte[] { 1 })) { }
            Require(File.Exists(Path.Combine(root, ".nbtcli-writer.lock")), "actual Stage/Dispose leaves its expected lock marker");
            var result = WorldDiscovery.Discover(new WorldInventory(root, World), new(SnapshotScope.All));
            Require(result.Complete && result.Sources.Any(source => source.Location.Artifact.RelativePath == ".nbtcli-writer.lock" && source.Outcome == SourceOutcome.Irrelevant),
                "normal writer lock is tool state, not a world-source read failure");
            CandidateIoTests.Reject<ArgumentException>(() => store.Read(new(World, ".nbtcli-writer.lock", ArtifactKind.Text)));
        }
        finally { Directory.Delete(root, true); }
    }
    public static void MissingDirectoryMetadata()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Directory.CreateDirectory(Path.Combine(root, "datapacks/empty"));
            Put(root, "datapacks/broken/readme.txt", Encoding.UTF8.GetBytes("No metadata"));
            Put(root, "datapacks/good/pack.mcmeta", Encoding.UTF8.GetBytes(LegacyPack));
            var inventory = new WorldInventory(root, World); var result = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
            Require(!result.Complete && new[] { "datapacks/empty", "datapacks/broken" }.All(path => result.Sources.Any(source => source.Location.Artifact.RelativePath == path && source.Outcome == SourceOutcome.Failed))
                && result.Sources.Any(source => source.Location.Artifact.RelativePath == "datapacks/good/pack.mcmeta" && source.Outcome == SourceOutcome.Success),
                "empty and text-only directory packs retain explicit root failures without suppressing good pack");
            var selected = WorldDiscovery.Discover(inventory, new(new([], [], [], ["datapacks/broken/readme.txt"])));
            Require(!selected.Complete && selected.Sources.Single().Outcome == SourceOutcome.Failed, "selected irrelevant member still depends on its pack metadata");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ExactScopePrecedesLocation()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "region/r.bad.0.mca", []); Directory.CreateDirectory(Path.Combine(root, "datapacks/empty"));
            var result = WorldDiscovery.Discover(new WorldInventory(root, World), new(new([], [], [], ["level.dat"])));
            Require(result.Complete && result.Sources.Single().Location.Artifact.RelativePath == "level.dat", "excluded malformed region and invalid pack cannot pollute exact physical scope");
        }
        finally { Directory.Delete(root, true); }
    }
}
