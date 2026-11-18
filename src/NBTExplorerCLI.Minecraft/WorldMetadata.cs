using System.Collections.Immutable;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Minecraft;

public sealed record WorldMetadata(CapabilityProfile Profile, int DataVersion, int WorldGenDataVersion, ImmutableDictionary<string, string> DimensionDirectories)
{
    public static WorldMetadata Read(NbtDocument level, NbtDocument? separateWorldGen = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        var version = Version(At(level.Root, "Data", "DataVersion"));
        var profile = MinecraftProfiles.ForDataVersion(version) ?? throw Unsupported("Unknown world DataVersion");
        var claim = At(level.Root, "Data", "Version", "Id");
        if (claim is not null && (claim is not NbtInt claimed || claimed.Value != version))
            throw new CodecException(CodecError.InvalidTag, 0, "Conflicting world version evidence");
        var series = At(level.Root, "Data", "Version", "Series");
        if (series is not null && series is not NbtString { Value: "main" }) throw Unsupported("Unknown world version series");
        var directoryLayout = profile.Features[ProfileFacet.Layout].Schema == "java-dimensions-directory";
        var registryVersion = version;
        NbtValue? registry;
        if (directoryLayout) registry = At(level.Root, "Data", "WorldGenSettings", "dimensions");
        else
        {
            if (separateWorldGen is null) throw Unsupported("Missing separately stored dimension registry");
            registryVersion = Version(At(separateWorldGen.Root, "DataVersion"));
            if (MinecraftProfiles.ForDataVersion(registryVersion) is null) throw Unsupported("Unknown dimension-registry DataVersion");
            registry = At(separateWorldGen.Root, "data", "dimensions");
        }
        if (registry is not NbtCompound dimensions) throw Unsupported("Missing declared dimension registry");
        if (dimensions.Entries.Length > 100_000) throw new CodecException(CodecError.LimitExceeded, 0, "Dimension registry exceeds source catalog budget");
        var roots = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dimension in dimensions.Entries)
        {
            var fields = dimension.Name.Split(':');
            if (fields.Length != 2 || fields.Any(field => field.Length == 0)
                || fields[0].Any(character => !NameCharacter(character))
                || fields[1].Any(character => character != '/' && !NameCharacter(character))
                || fields[1].Split('/').Any(part => part is "" or "." or "..") || !ids.Add(dimension.Name))
                throw new ArgumentException("Invalid or duplicate dimension identity");
            var directory = directoryLayout ? dimension.Name switch
            {
                "minecraft:overworld" => "", "minecraft:the_nether" => "DIM-1", "minecraft:the_end" => "DIM1",
                _ => "dimensions/" + fields[0] + "/" + fields[1]
            } : "dimensions/" + fields[0] + "/" + fields[1];
            if (directory.Length != 0) ArtifactAddress.ValidatePath(directory, entry: false);
            roots.Add(directory, dimension.Name);
        }
        return new(profile, version, registryVersion, roots.ToImmutable());
    }
    private static bool NameCharacter(char character) => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.';
    private static int Version(NbtValue? value) => value switch
    {
        NbtInt number => number.Value,
        null => throw Unsupported("Missing DataVersion"),
        _ => throw new CodecException(CodecError.InvalidTag, 0, "DataVersion must be an Int")
    };
    private static NbtValue? At(NbtValue root, params string[] keys)
    {
        var result = NbtLocator.Resolve(root, new(keys.Select(PathSegment.ForKey)));
        return result.Status switch
        {
            ResolutionStatus.Found => result.Value, ResolutionStatus.Missing => null,
            _ => throw new CodecException(CodecError.InvalidTag, 0, "Ambiguous or incorrectly typed metadata path")
        };
    }
    private static CodecException Unsupported(string message) => new(CodecError.UnsupportedProfile, 0, message);
}
