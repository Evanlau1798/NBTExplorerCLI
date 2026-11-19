using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

public interface IResourceOutputScope
{
    IDisposable GuardResourceOutput(ResourceGeneration generation);
}

public static class ResourceOutputPolicy
{
    private static readonly string[] GameplayRoots = ["data", "datapacks", "region", "entities", "poi", "players", "playerdata", "dimensions", "DIM1", "DIM-1", "stats", "advancements", "generated"];
    private static readonly string[] WorldFiles = ["level.dat", "level.dat_old", "level.dat_mcr", "session.lock", "uid.dat"];
    public static void ValidateWorldAncestor(IEnumerable<string> relativeOutputs, byte[] level, byte[]? worldGen)
    {
        var limits = DecodeLimits.Default;
        var metadata = WorldMetadata.Read(CompressedNbt.ReadDocument(level, NbtCompression.GZip, limits).Document,
            worldGen is null ? null : CompressedNbt.ReadDocument(worldGen, NbtCompression.GZip, limits).Document);
        if (metadata.Profile == MinecraftProfiles.ClassicAnvil)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Classic Anvil is read-only");
        if (MinecraftProfiles.IsReadOnlyAnvil(metadata.Profile))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Early versioned Anvil is read-only");
        // Known profiles map every non-root dimension below DIM1, DIM-1 or dimensions.
        var protectedRoots = GameplayRoots.Concat(WorldFiles);
        foreach (var path in relativeOutputs)
        {
            if (protectedRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("Resource generation cannot target world metadata or a gameplay subtree");
        }
    }
    internal static IDisposable? Guard(IWorldInventory inventory, PatchPlan plan)
    {
        if (plan.Operations.OfType<ResourcePatch>().SingleOrDefault() is not { } resource) return null;
        try { return Guard(inventory, resource.Generation); }
        catch (Exception error) when (error is NotSupportedException or CodecException or ArgumentException)
        { throw new ArtifactConflictException("Resource output scope is no longer valid: " + error.Message); }
    }
    internal static IDisposable Guard(IWorldInventory inventory, ResourceGeneration generation)
        => inventory is IResourceOutputScope scope ? scope.GuardResourceOutput(generation)
            : throw new NotSupportedException("Resource output requires physical scope inspection");
    internal static void CheckCatalog(IWorldInventory inventory, PatchPlan plan)
    {
        if (plan.Operations.OfType<ResourcePatch>().SingleOrDefault() is not { Generation.Format: ResourceOutputFormat.DirectoryPack } resource) return;
        var prefix = resource.Generation.Destination + "/";
        var catalog = inventory.Enumerate();
        if (!catalog.Complete) throw new IOException("Resource output catalog is incomplete");
        foreach (var directory in catalog.DirectoryPaths.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            if (!directory.StartsWith(prefix, StringComparison.Ordinal) || !ResourceInputService.IsResourceDirectory(directory[prefix.Length..]))
                throw new ArtifactConflictException("Resource pack directory set contains an unsupported subtree");
        var expected = plan.Preconditions.Where(input => input.Artifact.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            .Select(input => input.Artifact.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries.Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            if (!IsCandidate(entry.RelativePath) && (!expected.Contains(entry.RelativePath) || entry.IsDirectory))
                throw new ArtifactConflictException("Resource pack file set changed outside its complete plan");
    }
    internal static bool IsCandidate(string path) => path.AsSpan(path.LastIndexOf('/') + 1).StartsWith(".nbtcli-", StringComparison.OrdinalIgnoreCase);
}
