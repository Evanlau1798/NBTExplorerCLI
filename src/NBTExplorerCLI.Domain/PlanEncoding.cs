namespace NBTExplorerCLI.Domain;

internal static class PlanEncoding
{
    public static Hash256 Hash(PatchPlan plan)
    {
        var writer = new ContractWriter(PatchPlan.SchemaFamily);
        writer.Int(PatchPlan.SchemaVersion);
        writer.Text(plan.WorldId.ToString("D"));
        writer.Identity(plan.SnapshotId);
        writer.Text(plan.ProfileVersion);
        writer.Text(plan.PolicyVersion);
        writer.Byte((byte)plan.Recovery);
        writer.Int(plan.Operations.Length);
        foreach (var operation in plan.Operations) OperationEncoding.Write(writer, operation);
        writer.Int(plan.Preconditions.Length);
        foreach (var input in plan.Preconditions) { writer.Artifact(input.Artifact); writer.Image(input.Image); }
        writer.Int(plan.Outputs.Length);
        foreach (var output in plan.Outputs)
        {
            writer.Artifact(output.Artifact);
            writer.Byte((byte)output.Intent);
            writer.Int(output.RequiredChecks.Length);
            foreach (var check in output.RequiredChecks) writer.Text(check);
        }
        writer.Int(plan.Dependencies.Length);
        foreach (var dependency in plan.Dependencies) { writer.Artifact(dependency.Consumer); writer.Artifact(dependency.Required); }
        return writer.Finish();
    }
    public static Hash256 Hash(PreparedManifest manifest)
    {
        var writer = new ContractWriter(PreparedManifest.SchemaFamily);
        writer.Int(PreparedManifest.SchemaVersion);
        writer.Hash(manifest.PlanHash);
        writer.Int(manifest.Artifacts.Length);
        foreach (var artifact in manifest.Artifacts)
        {
            writer.Artifact(artifact.Artifact);
            writer.Optional(artifact.CandidatePath);
            writer.Image(artifact.Postimage);
            writer.Int(artifact.Checks.Length);
            foreach (var check in artifact.Checks)
            {
                writer.Text(check.Name);
                writer.Byte(check.Passed ? (byte)1 : (byte)0);
                writer.Hash(check.EvidenceHash);
            }
        }
        return writer.Finish();
    }
}
