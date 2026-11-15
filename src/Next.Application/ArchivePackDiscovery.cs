using System.IO.Compression;
using Next.Domain;
using Next.Formats;
using Next.Minecraft;

namespace Next.Application;

internal static class ArchivePackDiscovery
{
    internal static bool Discover(WorldSourceLocation location, byte[] bytes, DecodeLimits limits, Func<DiscoveredSource, bool> add)
    {
        using var archive = ZipArchiveValidation.OpenRead(bytes, limits);
        var root = location.Artifact.RelativePath;
        var kind = root.StartsWith("datapacks/", StringComparison.Ordinal) ? PackSourceKind.Datapack
            : archive.Entries.Any(entry => entry.FullName.StartsWith("assets/", StringComparison.Ordinal)) ? PackSourceKind.ResourcePack : PackSourceKind.Datapack;
        PackMetadata? metadata = null; SourceOutcome outcome = SourceOutcome.Success; string? code = null;
        try
        {
            var meta = archive.GetEntry("pack.mcmeta") ?? throw new InvalidDataException("Missing pack metadata");
            metadata = PackMetadata.Read(Read(meta), kind, limits);
        }
        catch (Exception error) when (DiscoveryFailure.IsExpected(error))
        { var failure = DiscoveryFailure.Describe(error); outcome = failure.Outcome; code = "PackMetadata:" + failure.Code; }
        if (!add(new(location with { PackKind = kind }, null, outcome, code, Hash256.Compute(bytes), new(Read: SupportLevel.Supported), metadata))) return false;
        foreach (var entry in archive.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (entry.FullName.EndsWith('/')) continue;
            var artifact = new ArtifactAddress(location.Artifact.WorldId, root, ArtifactKind.Archive, entry.FullName);
            var parts = entry.FullName.Split('/');
            var role = entry.FullName == "pack.mcmeta" ? WorldSourceRole.PackMetadata
                : parts.Length == 4 && parts[0] == "assets" && parts[2] == "lang" && parts[3].EndsWith(".json", StringComparison.Ordinal) ? WorldSourceRole.Language
                : parts.Length >= 4 && parts[0] == "data" && parts[2] is "function" or "functions" && entry.FullName.EndsWith(".mcfunction", StringComparison.Ordinal) ? WorldSourceRole.Function
                : parts.Length >= 4 && parts[0] == "data" && parts[2] is "structure" or "structures" && entry.FullName.EndsWith(".nbt", StringComparison.Ordinal) ? WorldSourceRole.Structure
                : parts.Length >= 4 && parts[0] is "data" or "assets" && entry.FullName.EndsWith(".json", StringComparison.Ordinal) ? WorldSourceRole.PackJson
                : WorldSourceRole.Irrelevant;
            var state = role == WorldSourceRole.Irrelevant ? SourceOutcome.Irrelevant : outcome;
            if (!add(new(new(artifact, role, PackRoot: root, PackKind: kind), null, state, role == WorldSourceRole.Irrelevant ? null : code,
                Hash256.Compute(Read(entry)), new(Read: SupportLevel.Supported), metadata))) return false;
        }
        return true;
    }
    private static byte[] Read(ZipArchiveEntry entry)
    {
        var bytes = new byte[checked((int)entry.Length)]; using var input = entry.Open(); input.ReadExactly(bytes); return bytes;
    }
}
