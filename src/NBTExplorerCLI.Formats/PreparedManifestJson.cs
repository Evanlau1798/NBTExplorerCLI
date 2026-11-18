using NBTExplorerCLI.Domain;
using System.Text.Json;
using static NBTExplorerCLI.Formats.ContractJson;

namespace NBTExplorerCLI.Formats;

public static class PreparedManifestJson
{
    private sealed record PreparedDto(ArtifactAddress? Artifact, string? CandidatePath, ArtifactImage? Postimage, ValidationCheck[]? Checks);
    private sealed record ManifestDto(Hash256? PlanHash, PreparedDto?[]? Artifacts, Hash256? ManifestHash);
    public static string Write(PreparedManifest prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return ContractJson.Write(PreparedManifest.SchemaFamily, new ManifestDto(prepared.PlanHash, prepared.Artifacts.Select(artifact =>
            new PreparedDto(artifact.Artifact, artifact.CandidatePath, artifact.Postimage, artifact.Checks.ToArray())).ToArray(), prepared.ManifestHash));
    }
    public static PreparedManifest Read(string json, PatchPlan plan) => ContractJson.Read(json, PreparedManifest.SchemaFamily, data =>
    {
        ArgumentNullException.ThrowIfNull(plan);
        var dto = Decode<ManifestDto>(data);
        if (dto.PlanHash != plan.PlanHash) throw new JsonException("Prepared input belongs to a different plan");
        var prepared = new PreparedManifest(plan, Required(dto.Artifacts).Select(item =>
        {
            var artifact = Required(item);
            return new PreparedArtifact(Required(artifact.Artifact), artifact.CandidatePath, Required(artifact.Postimage), Required(artifact.Checks));
        }));
        return prepared.ManifestHash == dto.ManifestHash ? prepared : throw new JsonException("Prepared hash differs from its payload");
    });
}
