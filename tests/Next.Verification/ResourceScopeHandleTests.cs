using System.Diagnostics;
using System.Text.Json;
using Next.Domain;
using Next.Infrastructure;
using Next.Formats;
using Next.Application;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class ResourceScopeHandleTests
{
    public static void DeepScopeRetainsALinearNumberOfNativeHandles(string? artifactRoot)
    {
        var measurements = new List<object>(); var failures = new List<string>();
        foreach (var nestedWorlds in new[] { false, true })
        foreach (var relativePlacement in new[] { false, true })
        {
            var scratch = ScratchDirectory.CreateChild();
            try
            {
                var root = scratch;
                for (var index = 0; index < 48; index++)
                {
                    if (nestedWorlds) Level(root, true);
                    root = Path.Combine(root, "a"); Directory.CreateDirectory(root);
                }
                var inventory = new WorldInventory(relativePlacement ? scratch : root, World);
                var destination = relativePlacement ? Path.GetRelativePath(scratch, root).Replace('\\', '/') + "/resources.zip" : "resources.zip";
                var generation = new ResourceGeneration(World, ResourceOutputFormat.ZipPack, destination, [new("zh_tw", [])], new(34, 0));
                using (var warm = new WorldInventory(scratch, World).GuardResourceOutput(generation)) { }
                using var process = Process.GetCurrentProcess(); process.Refresh(); var before = process.HandleCount;
                int during;
                using (var scope = inventory.GuardResourceOutput(generation)) { process.Refresh(); during = process.HandleCount; }
                process.Refresh(); var after = process.HandleCount;
                var depth = root.Count(unit => unit == Path.DirectorySeparatorChar); var budget = 8 * depth + 64;
                measurements.Add(new { kind = "live-native-handle-delta", nestedWorlds, relativePlacement, depth, before, during, after, retained = during - before, budget });
                if (during - before > budget || after - before > 16) failures.Add($"nestedWorlds={nestedWorlds}, relativePlacement={relativePlacement}, depth={depth}, retained={during - before}, budget={budget}, afterDelta={after - before}");
            }
            finally { Directory.Delete(scratch, true); }
        }
        if (artifactRoot is not null)
        {
            Directory.CreateDirectory(artifactRoot); File.WriteAllText(Path.Combine(artifactRoot, "resource-scope-handles.json"), JsonSerializer.Serialize(measurements));
        }
        Require(failures.Count == 0, "scope native handles must remain linear in depth and release after disposal: " + string.Join("; ", failures));
    }
    public static void BranchesRequireHeldAncestorsAndRejectMetadataReparsePoints()
    {
        var scratch = ScratchDirectory.CreateChild();
        try
        {
            var root = Path.Combine(scratch, "root"); var sibling = Path.Combine(scratch, "sibling"); Directory.CreateDirectory(root); Directory.CreateDirectory(sibling);
            using (var lease = GuardedArtifactPath.AcquireDirectory(root, ""))
            {
                foreach (var attempt in new[] { (Ancestor: sibling, Branch: "data"), (Ancestor: root, Branch: "../sibling") })
                {
                    var refused = false; try { lease.HoldExistingBranch(attempt.Ancestor, attempt.Branch); } catch (ArgumentException) { refused = true; }
                    Require(refused, "branch access cannot escape the directory ancestry already held by the lease");
                }
                var missing = false; try { lease.HoldExistingBranch(root, "missing/child"); } catch (FileNotFoundException) { missing = true; }
                Require(missing && !Directory.Exists(Path.Combine(root, "missing")), "read-only branch leases never create missing directories");
            }
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(4774), NbtCompression.GZip));
            Put(sibling, "minecraft/world_gen_settings.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Separate(4774, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Directory.CreateSymbolicLink(Path.Combine(root, "data"), sibling);
            var generation = new ResourceGeneration(World, ResourceOutputFormat.ZipPack, "resources.zip", [new("zh_tw", [])], new(34, 0));
            var rejected = false;
            try { using var scope = new WorldInventory(root, World).GuardResourceOutput(generation); } catch (IOException) { rejected = true; }
            Require(rejected, "modern metadata branches cannot follow reparse points even when the target has valid registry bytes");
        }
        finally { Directory.Delete(scratch, true); }
    }
    public static void MetadataProtectionDoesNotMultiplyDimensionAndOutputAllocations(string? artifactRoot)
    {
        const int dimensionCount = 2000, outputCount = 1000;
        var dimensions = new NbtCompound(Enumerable.Range(0, dimensionCount).Select(index => new NbtEntry("test:dim" + index, new NbtCompound([]))));
        var level = CompressedNbt.WriteDocument(WorldMetadataTests.Level(3953, dimensions), NbtCompression.GZip);
        var paths = Enumerable.Range(0, outputCount).Select(index => "resources/asset" + index + ".json").ToArray();
        ResourceOutputPolicy.ValidateWorldAncestor(["resources/warm.zip"], level, null);
        var budget = 16_777_216 + level.LongLength * 16 + paths.Sum(path => (long)path.Length * 2) * 16;
        var before = GC.GetAllocatedBytesForCurrentThread(); ResourceOutputPolicy.ValidateWorldAncestor(paths, level, null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var refused = false; try { ResourceOutputPolicy.ValidateWorldAncestor(["dimensions/test/dim500/data/new.dat"], level, null); } catch (NotSupportedException) { refused = true; }
        if (artifactRoot is not null)
        {
            Directory.CreateDirectory(artifactRoot); File.WriteAllText(Path.Combine(artifactRoot, "resource-scope-allocation.json"),
                JsonSerializer.Serialize(new { kind = "current-thread-cumulative-allocation", dimensionCount, outputCount, inputBytes = level.Length, allocated, budget, customDimensionRefused = refused }));
        }
        Require(refused && allocated <= budget, $"scope allocation must scale with bounded metadata and output inputs without a dimension/output cross product: allocated={allocated}, budget={budget}, dimensionRefused={refused}; this is not peak RSS");
    }
}
