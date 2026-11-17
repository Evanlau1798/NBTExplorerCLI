using Next.Domain;
using Next.Formats;

namespace Next.Application;

public sealed class ResourceOutputService(IWorldInventory inventory, IArtifactStorage storage, IWorldBackupStore objects)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IArtifactStorage storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly IWorldBackupStore objects = objects ?? throw new ArgumentNullException(nameof(objects));
    public PatchPlan Build(ResourceGeneration generation) => Bind(generation).Plan;
    public PreparedWorldPlan Prepare(PatchPlan plan, bool worldOffline)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!worldOffline) throw new ArgumentException("Resource preparation requires explicit offline acknowledgement");
        if (plan.Operations is not [{ } operation] || operation is not ResourcePatch resource) throw new ArgumentException("Resource preparation requires one explicit resource operation");
        using var scope = ResourceOutputPolicy.Guard(inventory, resource.Generation);
        var bound = Bind(resource.Generation);
        if (bound.Plan.PlanHash != plan.PlanHash) throw new ArtifactConflictException("Resource plan differs from its exact current inputs and outputs");
        return new WorldPlanPreparation(inventory, storage).PrepareOutputs(plan, (output, _) =>
        {
            var bytes = bound.Files[output.Artifact.RelativePath]; var hash = Hash256.Compute(bytes);
            return new(bytes, stream =>
            {
                if (new Hash256(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))) != hash)
                    throw new ArtifactConflictException("Resource candidate differs from its complete validated projection");
            });
        });
    }
    private (PatchPlan Plan, Dictionary<string, byte[]> Files) Bind(ResourceGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (generation.WorldId != inventory.WorldId) throw new ArgumentException("Resource recipe belongs to another output scope");
        using var scope = ResourceOutputPolicy.Guard(inventory, generation);
        var access = inventory.AccessBinding;
        if (access is null || access != storage.AccessBinding) throw new ArgumentException("Resource inventory and storage must bind the same physical root");
        var catalog = inventory.Enumerate(); if (!catalog.Complete) throw new IOException("Resource output catalog is incomplete");
        var selected = generation.Outputs.Select(output => output.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ResourceInputService.RequireByteBudget(catalog.Entries.Where(entry => selected.Contains(entry.RelativePath)
            || generation.Format == ResourceOutputFormat.DirectoryPack && entry.RelativePath.StartsWith(generation.Destination + "/", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Bytes));
        var inputs = new Dictionary<string, ArtifactPrecondition>(StringComparer.Ordinal); var existing = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long readBytes = 0;
        byte[]? Read(ArtifactAddress artifact)
        {
            byte[]? bytes; try { bytes = inventory.Read(artifact); } catch (FileNotFoundException) { bytes = null; }
            if (bytes is not null) { ResourceInputService.RequireByteBudget([readBytes, bytes.Length]); readBytes += bytes.Length; }
            inputs[artifact.RelativePath] = new(artifact, bytes is null ? ArtifactImage.Absent : ArtifactImage.Present(Hash256.Compute(bytes))); return bytes;
        }
        byte[]? originalZip = null;
        if (generation.Format == ResourceOutputFormat.DirectoryPack)
        {
            var prefix = generation.Destination + "/"; long retained = 0;
            if (catalog.Entries.Any(entry => entry.RelativePath.Equals(generation.Destination, StringComparison.OrdinalIgnoreCase))) throw new ArtifactConflictException("Pack directory collides with a physical file");
            if (catalog.DirectoryPaths.Any(path => path.Equals(generation.Destination, StringComparison.OrdinalIgnoreCase) && path != generation.Destination))
                throw new ArtifactConflictException("Pack directory aliases an existing physical path");
            foreach (var directory in catalog.DirectoryPaths.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                if (!directory.StartsWith(prefix, StringComparison.Ordinal) || !ResourceInputService.IsResourceDirectory(directory[prefix.Length..]))
                    throw new NotSupportedException("Directory pack contains an unsupported non-resource subtree");
            foreach (var entry in catalog.Entries.Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (!entry.RelativePath.StartsWith(prefix, StringComparison.Ordinal) || entry.IsDirectory) throw new ArtifactConflictException("Pack path has a physical alias or unsupported member");
                if (ResourceOutputPolicy.IsCandidate(entry.RelativePath)) continue;
                var bytes = Read(new(inventory.WorldId, entry.RelativePath, Kind(entry.RelativePath[prefix.Length..]))) ?? throw new ArtifactConflictException("Resource member disappeared during binding");
                if ((retained += bytes.Length) > ResourceRendering.Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Existing resource pack exceeds aggregate byte budget");
                existing.Add(entry.RelativePath[prefix.Length..], bytes);
            }
        }
        foreach (var artifact in generation.Outputs)
        {
            if (inputs.ContainsKey(artifact.RelativePath)) continue;
            if (catalog.Entries.Any(entry => entry.RelativePath.Equals(artifact.RelativePath, StringComparison.OrdinalIgnoreCase) && entry.RelativePath != artifact.RelativePath))
                throw new ArtifactConflictException("Resource output aliases an existing physical path");
            var bytes = Read(artifact); if (bytes is null) continue;
            if (generation.Format == ResourceOutputFormat.ZipPack) { originalZip = bytes; existing = ResourceInputService.ZipFiles(bytes); }
            else existing.Add(generation.Format == ResourceOutputFormat.LanguageFiles ? artifact.RelativePath : artifact.RelativePath[(generation.Destination.Length + 1)..], bytes);
        }
        var merge = generation.Merge is null ? null : new ResourceInputService(objects).Load(generation);
        var rendered = ResourceRendering.Render(generation, existing, merge);
        var outputs = generation.Format == ResourceOutputFormat.ZipPack
            ? new Dictionary<string, byte[]> { [generation.Destination] = ResourceRendering.Zip(originalZip, rendered) }
            : generation.Outputs.ToDictionary(artifact => artifact.RelativePath,
                artifact => rendered[generation.Format == ResourceOutputFormat.LanguageFiles ? artifact.RelativePath : artifact.RelativePath[(generation.Destination.Length + 1)..]], StringComparer.Ordinal);
        var snapshot = SourceIdentity.Snapshot(inventory.WorldId, inputs.Values.Where(input => !input.Image.ExpectedAbsent)
            .Select(input => new ManifestSource(input.Artifact, input.Image.Hash, SourceOutcome.Success)), SnapshotScope.All,
            new("resource-input-v1", ResourceGeneration.Profile, "resource-render-v1", ResourceGeneration.Policy));
        var plan = new PatchPlan(inventory.WorldId, snapshot, ResourceGeneration.Profile, ResourceGeneration.Policy,
            [new ResourcePatch("resources", generation)], inputs.Values,
            generation.Outputs.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, ["resource-schema", "preimage", "profile", "projection"])), []);
        WorldPlanBuilder.CheckPreimages(inventory, plan); ResourceOutputPolicy.CheckCatalog(inventory, plan);
        if (inventory.AccessBinding != access || storage.AccessBinding != access) throw new ArtifactConflictException("Resource root changed during binding");
        return (plan, outputs);
    }
    private static ArtifactKind Kind(string path) => path.EndsWith(".json", StringComparison.Ordinal) || path == "pack.mcmeta" ? ArtifactKind.Json : ArtifactKind.Text;
}
