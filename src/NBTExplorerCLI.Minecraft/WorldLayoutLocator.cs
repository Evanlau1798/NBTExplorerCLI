using NBTExplorerCLI.Domain;
using System.Globalization;

namespace NBTExplorerCLI.Minecraft;

public static class WorldLayoutLocator
{
    public static WorldSourceLocation Locate(Guid worldId, string relativePath, CapabilityProfile profile,
        IReadOnlyDictionary<string, string> dimensions, IReadOnlyDictionary<string, PackSourceKind> packDirectories)
    {
        ArgumentNullException.ThrowIfNull(relativePath); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(dimensions); ArgumentNullException.ThrowIfNull(packDirectories);
        var parts = relativePath.Split('/'); var name = parts[^1];
        var extension = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : "";
        var kind = extension switch
        {
            "dat" or "nbt" => ArtifactKind.Nbt, "mca" or "mcr" => parts.Length >= 2 && parts[^2] == "entities" ? ArtifactKind.Entities : ArtifactKind.Region,
            "json" or "mcmeta" => ArtifactKind.Json, "mcfunction" => ArtifactKind.McFunction, "zip" => ArtifactKind.Archive, _ => ArtifactKind.Text
        };
        var artifact = new ArtifactAddress(worldId, relativePath, kind);
        if (!profile.Features.TryGetValue(ProfileFacet.Layout, out var layout) || layout.Operations.Read != SupportLevel.Supported
            || layout.Schema is not ("java-dimensions-directory" or "java-dimensions-namespaced"))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown world layout");
        if (relativePath == "level.dat") return new(artifact, WorldSourceRole.Level);
        if (kind == ArtifactKind.Text) return new(artifact, WorldSourceRole.Irrelevant);
        if (kind == ArtifactKind.Archive) return new(artifact, WorldSourceRole.Archive, PackRoot: relativePath);
        var directoryLayout = layout.Schema == "java-dimensions-directory";
        if (parts.Length >= 4 && parts[0] == "generated" && parts[2] == (directoryLayout ? "structures" : "structure") && extension == "nbt"
            && name[..^4] is not ("" or "." or "..")
            && MinecraftIdentifier.ResourceLocation(parts[1] + ":" + string.Join("/", parts, 3, parts.Length - 3)[..^4]))
            return new(artifact, WorldSourceRole.Structure);
        var progress = directoryLayout ? parts.Length == 2 && parts[0] is "advancements" or "stats"
            : parts.Length == 3 && parts[0] == "players" && parts[1] is "advancements" or "stats";
        if (progress && name.EndsWith(".json", StringComparison.Ordinal) && Guid.TryParseExact(name[..^5], "D", out _))
            return new(artifact, WorldSourceRole.Irrelevant);
        var classic = profile == MinecraftProfiles.ClassicAnvil;
        if (classic && parts.Length == 2 && parts[0] == "players" && extension == "dat") return new(artifact, WorldSourceRole.Player);
        if (classic && IsClassicChunkTickets(relativePath))
            return new(artifact, WorldSourceRole.SavedData, dimensions.GetValueOrDefault(parts.Length == 1 ? "" : parts[0]));
        if (kind == ArtifactKind.Nbt && parts.Length >= 2 && parts[0] == (directoryLayout ? "playerdata" : "players")) return new(artifact, WorldSourceRole.Player);
        if (parts.Length >= 2 && parts[0] == "data" && kind is ArtifactKind.Nbt or ArtifactKind.Json) return new(artifact, WorldSourceRole.SavedData);
        var matches = new List<WorldSourceLocation>();
        foreach (var directory in Ancestors(relativePath))
        {
            if (!dimensions.TryGetValue(directory, out var dimension)) continue;
            if (directory.Length == 0 && !directoryLayout) continue;
            var storage = Under(relativePath, directory);
            if (storage is null) continue;
            var segments = storage.Split('/');
            if (segments.Length == 2 && segments[0] is "region" or "entities" or "poi" && kind is ArtifactKind.Region or ArtifactKind.Entities)
            {
                var coordinates = RegionCoordinates(name);
                var role = segments[0] switch { "region" => WorldSourceRole.Terrain, "entities" => WorldSourceRole.Entities, _ => WorldSourceRole.PointOfInterest };
                if (classic && role == WorldSourceRole.Terrain && extension == "mcr") role = WorldSourceRole.Irrelevant;
                matches.Add(new(artifact, role, dimension, coordinates.X, coordinates.Z));
            }
            else if (segments.Length >= 2 && segments[0] == "data" && kind is ArtifactKind.Nbt or ArtifactKind.Json)
                matches.Add(new(artifact, WorldSourceRole.SavedData, dimension));
        }
        if (matches.Count > 1) throw new ArgumentException("Ambiguous dimension ownership", nameof(dimensions));
        if (matches.Count == 1) return matches[0];
        var packs = Ancestors(relativePath).Where(packDirectories.ContainsKey)
            .Select(root => (Root: root, Kind: packDirectories[root], Entry: Under(relativePath, root))).ToArray();
        if (packs.Length > 1) throw new ArgumentException("Ambiguous directory pack ownership", nameof(packDirectories));
        if (packs.Length == 1)
        {
            var pack = packs[0]; var entry = pack.Entry!; var segments = entry.Split('/');
            var role = entry == "pack.mcmeta" ? WorldSourceRole.PackMetadata
                : segments.Length >= 4 && segments[0] == "assets" && segments[2] == "lang" && kind == ArtifactKind.Json ? WorldSourceRole.Language
                : segments.Length >= 4 && segments[0] == "data" && kind == ArtifactKind.McFunction ? WorldSourceRole.Function
                : segments.Length >= 4 && segments[0] == "data" && segments[2] is "structure" or "structures" && kind == ArtifactKind.Nbt ? WorldSourceRole.Structure
                : segments.Length >= 3 && segments[0] is "data" or "assets" && kind == ArtifactKind.Json ? WorldSourceRole.PackJson
                : WorldSourceRole.Unknown;
            return new(artifact, role, PackRoot: pack.Root, PackKind: pack.Kind);
        }
        return new(artifact, WorldSourceRole.Unknown);
    }

    public static string? FunctionId(WorldSourceLocation location)
    {
        var entry = location.Artifact.ContainerEntry ?? (location.PackRoot is { } root ? Under(location.Artifact.RelativePath, root) : null);
        if (entry is null || !entry.EndsWith(".mcfunction", StringComparison.Ordinal)) return null;
        var segments = entry[..^".mcfunction".Length].Split('/', 4);
        if (segments.Length != 4 || segments[0] != "data" || segments[2] is not ("function" or "functions")) return null;
        var id = segments[1] + ":" + segments[3];
        return MinecraftIdentifier.ResourceLocation(id) ? id : null;
    }

    public static string? CommandStorageNamespace(CapabilityProfile layout, string relativePath, bool ignoreCase = false)
    {
        var namespaced = layout.Features[ProfileFacet.Layout].Schema == "java-dimensions-namespaced";
        var prefix = namespaced ? "data/" : "data/command_storage_";
        var suffix = namespaced ? "/command_storage.dat" : ".dat";
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (relativePath.Length <= prefix.Length + suffix.Length
            || !relativePath.StartsWith(prefix, comparison) || !relativePath.EndsWith(suffix, comparison)) return null;
        var space = relativePath[prefix.Length..^suffix.Length];
        if (ignoreCase) space = space.ToLowerInvariant();
        return MinecraftIdentifier.ResourceLocation(space + ":storage") ? space : null;
    }

    internal static bool IsClassicChunkTickets(string path) => path is "forcedchunks.dat" or "DIM-1/forcedchunks.dat" or "DIM1/forcedchunks.dat";
    private static string? Under(string path, string directory)
        => directory.Length == 0 ? path : path.StartsWith(directory + "/", StringComparison.Ordinal) ? path[(directory.Length + 1)..] : null;
    private static IEnumerable<string> Ancestors(string path)
    {
        yield return "";
        for (var index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1)) yield return path[..index];
    }
    private static (int X, int Z) RegionCoordinates(string name)
    {
        var fields = name.Split('.');
        if (fields.Length != 4 || fields[0] != "r" || fields[3] is not ("mca" or "mcr")
            || !int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)
            || x is < -67_108_864 or > 67_108_863 || z is < -67_108_864 or > 67_108_863)
            throw new CodecException(CodecError.InvalidRegion, 0, "Invalid or unrepresentable region coordinates");
        return (x, z);
    }
}
