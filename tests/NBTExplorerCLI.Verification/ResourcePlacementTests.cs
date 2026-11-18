using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class ResourcePlacementTests
{
    public static void RelativeOutputDirectoriesAreExactPlanPathsAndAppearOnlyAtCommit()
    {
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var destination = "out/nested" + (format == ResourceOutputFormat.ZipPack ? "/resources.zip" : format == ResourceOutputFormat.DirectoryPack ? "/Bundle" : "");
                var generation = new ResourceGeneration(World, format, destination, [new("zh_tw", [new("map.test", "譯文")])], format == ResourceOutputFormat.LanguageFiles ? null : new(34, 0));
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World); var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
                var service = new ResourceOutputService(inventory, storage, objects); var plan = service.Build(generation);
                Require(plan.Outputs.All(output => output.Artifact.RelativePath.StartsWith("out/nested/", StringComparison.Ordinal))
                    && PatchPlanJson.Read(PatchPlanJson.Write(plan)).PlanHash == plan.PlanHash && !Directory.Exists(Path.Combine(root, "out")), "relative placement is part of existing canonical destination/hash and preview never creates output parents");
                using var prepared = service.Prepare(plan, true);
                Require(!Directory.Exists(Path.Combine(root, "out")), "complete staging still retains missing parent directories until explicit commit");
                var result = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals"))).Apply(prepared, true);
                Require(result.Success && plan.Outputs.All(output => File.Exists(Path.Combine(root, output.Artifact.RelativePath))), "existing parent-bound native candidates create the exact declared resource paths at commit");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    public static void NestedWorldGameplayCannotHideBelowThePhysicalOutputAnchor()
    {
        var failures = new List<string>();
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                WorldDiscoveryTests.Level(Path.Combine(root, "nested"), false);
                var destination = "nested/data" + (format == ResourceOutputFormat.ZipPack ? "/resources.zip" : format == ResourceOutputFormat.DirectoryPack ? "/Bundle" : "");
                var generation = new ResourceGeneration(World, format, destination, [new("zh_tw", [new("map.test", "譯文")])], format == ResourceOutputFormat.LanguageFiles ? null : new(34, 0));
                var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World); var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
                var service = new ResourceOutputService(inventory, storage, objects); var refused = false;
                try
                {
                    var plan = service.Build(generation);
                    _ = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals"))).Apply(service.Prepare(plan, true), true);
                }
                catch (Exception failure) when (failure is NotSupportedException or ArtifactConflictException) { refused = true; }
                if (!refused || generation.Outputs.Any(output => File.Exists(Path.Combine(root, output.RelativePath)))) failures.Add(format.ToString());
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
        Require(failures.Count == 0, "every actual output parent must be checked for descendant-world gameplay authority, including paths below the bound physical root: " + string.Join(',', failures));
    }
    public static void FlatDirectoryPackCannotCreateGameplayFilesInsideAnEmbeddedWorld()
    {
        var root = ScratchDirectory.CreateChild(); var source = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
        try
        {
            byte[] metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep\"}}"u8.ToArray();
            WorldDiscoveryTests.Put(root, "Bundle/pack.mcmeta", metadata); WorldDiscoveryTests.Level(Path.Combine(root, "Bundle/assets/test/world"), false);
            WorldDiscoveryTests.Put(source, "pack.mcmeta", metadata); WorldDiscoveryTests.Put(source, "assets/test/world/data/new.dat", [1, 2, 3]);
            var objects = new FileSystemWorldBackupStore(Path.Combine(state, "objects"));
            var request = new ResourceInputService(objects).Capture(new(World, ResourceOutputFormat.DirectoryPack, "Bundle", [new("zh_tw", [])], new(34, 0)), new WorldInventory(source, World));
            var inventory = new WorldInventory(root, World); var storage = new ArtifactStorage(root, World); var service = new ResourceOutputService(inventory, storage, objects); var refused = false;
            try
            {
                var plan = service.Build(request);
                _ = new WorldApplyEngine(inventory, storage, objects, new FileSystemApplyJournalStore(Path.Combine(state, "journals"))).Apply(service.Prepare(plan, true), true);
            }
            catch (Exception failure) when (failure is NotSupportedException or ArtifactConflictException) { refused = true; }
            Require(refused && !File.Exists(Path.Combine(root, "Bundle/assets/test/world/data/new.dat")), "even a previously valid flat-destination merge must not create raw gameplay data below a descendant world marker");
        }
        finally { Directory.Delete(root, true); Directory.Delete(source, true); Directory.Delete(state, true); }
    }
    public static void DescendantMalformedFileOrDirectoryMarkersFailClosed()
    {
        var failures = new List<string>();
        foreach (var directoryMarker in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                if (directoryMarker) Directory.CreateDirectory(Path.Combine(root, "nested/level.dat")); else WorldDiscoveryTests.Put(root, "nested/level.dat", [9]);
                var generation = new ResourceGeneration(World, ResourceOutputFormat.ZipPack, "nested/output/resources.zip", [new("zh_tw", [])], new(34, 0));
                var refused = false;
                try { _ = new ResourceOutputService(new WorldInventory(root, World), new ArtifactStorage(root, World), new FileSystemWorldBackupStore(state)).Build(generation); }
                catch (Exception failure) when (failure is CodecException or IOException or NotSupportedException) { refused = true; }
                if (!refused) failures.Add(directoryMarker ? "directory" : "malformed-file");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
        Require(failures.Count == 0, "descendant world marker shape cannot fail open: " + string.Join(',', failures));
    }
}
