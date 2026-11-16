using Next.Domain;
using System.Text.Json;
using System.Collections.Immutable;

namespace Next.Formats;

public static class LegacyImportJson
{
    private sealed record BundleDto(Hash256 ImportId, LegacyInputProfile Profile, int ImporterVersion, Hash256 InputHash, string RawInputBase64,
        Guid WorldId, StableIdentity SnapshotId, string SourceLocale, string TargetLocale, bool RebindAcknowledged, ImmutableArray<LegacyImportItem> Items);
    private static string Encode(LegacyImportBundle value) => ContractJson.Write("nbtcli.next.legacy-import", new BundleDto(value.ImportId, value.Profile, value.ImporterVersion, value.InputHash,
        value.RawInputBase64, value.WorldId, value.SnapshotId, value.SourceLocale, value.TargetLocale, value.RebindAcknowledged, value.Items));
    public static string Write(LegacyImportBundle bundle) { Validate(bundle); var json = Encode(bundle); _ = Read(json); return json; }
    public static LegacyImportBundle Read(string json) => ContractJson.Read(json, "nbtcli.next.legacy-import", data =>
    {
        ContractJson.Fields(data, "importId", "profile", "importerVersion", "inputHash", "rawInputBase64", "worldId", "snapshotId", "sourceLocale", "targetLocale", "rebindAcknowledged", "items");
        var value = ContractJson.Decode<LegacyImportBundle>(data); Validate(value);
        if (Encode(value) != json) throw new JsonException("Legacy report must be canonical"); return value;
    });
    private static void Validate(LegacyImportBundle value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.Profile) || value.ImporterVersion != 1 || value.WorldId == Guid.Empty || value.SnapshotId is not { Kind: IdentityKind.Snapshot, IsValid: true }
            || string.IsNullOrWhiteSpace(value.SourceLocale) || string.IsNullOrWhiteSpace(value.TargetLocale) || value.SourceLocale.Length > 128 || value.TargetLocale.Length > 128
            || value.RawInputBase64 is null || value.RawInputBase64.Length > 22_369_624 || value.Items.IsDefault || value.Items.Length is < 1 or > 100_000) throw new JsonException("Invalid legacy report binding");
        byte[] raw; try { raw = Convert.FromBase64String(value.RawInputBase64); } catch (FormatException error) { throw new JsonException("Invalid raw legacy bytes", error); }
        if (raw.Length > LegacyInputReader.MaximumInputBytes || Convert.ToBase64String(raw) != value.RawInputBase64 || Hash256.Compute(raw) != value.InputHash
            || LegacyImportIdentity.Compute(value.Profile, value.InputHash, value.WorldId, value.SnapshotId, value.SourceLocale, value.TargetLocale, value.RebindAcknowledged) != value.ImportId) throw new JsonException("Legacy input identity differs");
        long candidates = 0;
        for (var index = 0; index < value.Items.Length; index++)
        {
            var item = value.Items[index];
            if (item is null || item.Index != index || !Enum.IsDefined(item.Decision) || string.IsNullOrWhiteSpace(item.Reason) || item.Candidates.IsDefault || item.Candidates.Length > 100_000
                || item.Candidates.Any(address => address is null || address.Artifact.WorldId != value.WorldId)
                || item.Decision == LegacyImportDecision.Mapped && (item.Candidates.Length != 1 || item.CurrentSourceHash is null || item.SlotId is null || item.Original is null)) throw new JsonException("Invalid legacy record decision");
            candidates = checked(candidates + item.Candidates.Length);
            if (candidates > 4096) throw new JsonException("Legacy report exceeds aggregate candidate budget");
        }
    }
}
