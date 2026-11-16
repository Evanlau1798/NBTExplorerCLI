using Next.Domain;

namespace Next.Application;

internal static class WorldRestorePlans
{
    internal static PatchPlan Build(IWorldInventory inventory, VerifiedBackup backup, IEnumerable<RestorePatch> operations)
    {
        ArgumentNullException.ThrowIfNull(backup); ArgumentNullException.ThrowIfNull(operations);
        if (backup.Manifest.WorldId != inventory.WorldId) throw new ArtifactConflictException("Backup belongs to another world");
        var requested = operations.Take(100_001).ToArray();
        if (requested.Length is 0 or > 100_000 || requested.Any(operation => operation is null)) throw new ArgumentException("Invalid raw restore operation set", nameof(operations));
        var authorized = backup.Manifest.Artifacts.ToDictionary(row => row.Artifact);
        var inputs = new List<ArtifactPrecondition>(); var outputs = new List<OutputDeclaration>();
        foreach (var operation in requested)
        {
            if (operation.Restore.VerifiedManifestHash != backup.ManifestHash) throw new ArtifactConflictException("Restore manifest differs from the verified backup");
            foreach (var target in operation.Restore.AuthorizedArtifacts)
            {
                if (outputs.Count == 100_000) throw new ArgumentException("Restore exceeds the aggregate artifact budget", nameof(operations));
                if (!authorized.TryGetValue(target.Artifact, out var original)) throw new ArgumentException("Restore artifact is not authorized by the verified backup", nameof(operations));
                if (original.Image != target.Image) throw new ArtifactConflictException("Restore target differs from the verified backup image");
                _ = backup.ReadArtifact(target.Artifact);
                inputs.Add(new(target.Artifact, operation.Restore.CurrentImages[target.Artifact]));
                outputs.Add(new(target.Artifact, target.Image.ExpectedAbsent ? OutputIntent.Delete : OutputIntent.Write, ["preimage", "raw-image"]));
            }
        }
        // Absence stays in the raw plan preconditions; it is not a successful parsed source.
        var snapshot = SourceIdentity.Snapshot(inventory.WorldId, inputs.Where(input => !input.Image.ExpectedAbsent)
            .Select(input => new ManifestSource(input.Artifact, input.Image.Hash, SourceOutcome.Success)),
            new([], [], [], outputs.Select(output => output.Artifact.RelativePath)), new("raw-v1", "raw-v1", backup.ManifestHash.Hex, "world-restore-v1"));
        var plan = new PatchPlan(inventory.WorldId, snapshot, "raw-v1", "world-restore-v1", requested, inputs, outputs, []);
        WorldPlanBuilder.CheckPreimages(inventory, plan); return plan;
    }
}
