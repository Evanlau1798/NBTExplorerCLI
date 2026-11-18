using NBTExplorerCLI.Domain;
using System.Text.Json;
using static NBTExplorerCLI.Formats.ContractJson;

namespace NBTExplorerCLI.Formats;

public static class PatchPlanJson
{
    private sealed record OutputDto(ArtifactAddress? Artifact, OutputIntent? Intent, string[]? RequiredChecks);
    private sealed record PlanDto(Guid? WorldId, StableIdentity? SnapshotId, string? ProfileVersion, string? PolicyVersion,
        RecoveryStrategy? Recovery, PatchOperation[]? Operations, ArtifactPrecondition[]? Preconditions, OutputDto?[]? Outputs,
        ArtifactDependency[]? Dependencies, Hash256? PlanHash);
    public static string Write(PatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContractJson.Write(PatchPlan.SchemaFamily, new PlanDto(plan.WorldId, plan.SnapshotId, plan.ProfileVersion, plan.PolicyVersion,
            plan.Recovery, plan.Operations.ToArray(), plan.Preconditions.ToArray(), plan.Outputs.Select(output => new OutputDto(output.Artifact,
                output.Intent, output.RequiredChecks.ToArray())).ToArray(), plan.Dependencies.ToArray(), plan.PlanHash));
    }
    public static PatchPlan Read(string json) => ContractJson.Read(json, PatchPlan.SchemaFamily, data =>
    {
        var dto = Decode<PlanDto>(data);
        var plan = new PatchPlan(dto.WorldId ?? throw new JsonException("Missing plan world"), Required(dto.SnapshotId), Required(dto.ProfileVersion),
            Required(dto.PolicyVersion), Required(dto.Operations), Required(dto.Preconditions), Required(dto.Outputs).Select(item =>
            {
                var output = Required(item);
                return new OutputDeclaration(Required(output.Artifact), output.Intent ?? throw new JsonException("Missing output intent"), Required(output.RequiredChecks));
            }), Required(dto.Dependencies), dto.Recovery ?? throw new JsonException("Missing recovery strategy"));
        return plan.PlanHash == dto.PlanHash ? plan : throw new JsonException("Plan hash differs from its semantic payload");
    });
}
