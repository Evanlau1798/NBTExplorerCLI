using Next.Domain;
using System.Text.Json;

namespace Next.Formats;

public static class BackupManifestJson
{
    private sealed record ManifestDto(Guid WorldId, BackupArtifact?[]? Artifacts);
    private static string Encode(BackupManifest manifest) => ContractJson.WriteBounded(BackupManifest.SchemaFamily, new ManifestDto(manifest.WorldId, manifest.Artifacts.Cast<BackupArtifact?>().ToArray()));
    public static string Write(BackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest); var json = Encode(manifest); _ = Read(json); return json;
    }
    public static BackupManifest Read(string json) => ContractJson.Read(json, BackupManifest.SchemaFamily, data =>
    {
        ContractJson.Fields(data, "worldId", "artifacts"); var dto = ContractJson.Decode<ManifestDto>(data);
        var manifest = new BackupManifest(dto.WorldId, ContractJson.Required(dto.Artifacts).Select(row => ContractJson.Required(row)));
        if (Encode(manifest) != json) throw new JsonException("Backup manifest must be canonical"); return manifest;
    });
}
