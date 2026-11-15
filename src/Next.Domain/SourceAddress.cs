using System.Globalization;

namespace Next.Domain;

public enum ArtifactKind { Nbt, Region, Entities, Json, McFunction, Archive, Text }
public enum JsonTargetKind { Value, PropertyName }

public sealed record ArtifactAddress
{
    public Guid WorldId { get; }
    public string RelativePath { get; }
    public ArtifactKind Kind { get; }
    public string? ContainerEntry { get; }
    public ArtifactAddress(Guid worldId, string relativePath, ArtifactKind kind, string? containerEntry = null)
    {
        if (worldId == Guid.Empty) throw new ArgumentException("World identity is required", nameof(worldId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ValidatePath(relativePath, false);
        if (containerEntry is not null)
        {
            if (kind != ArtifactKind.Archive) throw new ArgumentException("Entry requires an archive artifact", nameof(containerEntry));
            ValidatePath(containerEntry, true);
        }
        (WorldId, RelativePath, Kind, ContainerEntry) = (worldId, relativePath, kind, containerEntry);
    }

    public static void ValidatePath(string path, bool entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var parts = path.Split('/');
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains('\0')
            || parts.Any(part => part is "" or "." or "..")
            || (!entry && (path.Any(character => character < 32 || "<>:\"|?*".Contains(character))
                || parts.Any(part => part.EndsWith('.') || part.EndsWith(' '))))
            || (entry && path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
            throw new ArgumentException("Invalid or ambiguous relative artifact/entry path", nameof(path));
    }
}

public sealed record ChunkPosition(int X, int Z)
{
    public int RegionX => X >> 5;
    public int RegionZ => Z >> 5;
    public int LocalX => X & 31;
    public int LocalZ => Z & 31;
}

public sealed record SyntaxLocator
{
    public string GrammarProfile { get; }
    public int GrammarVersion { get; }
    public long StartByte { get; }
    public long LengthBytes { get; }
    public string PreimageHash { get; }
    public string Encoding { get; }
    public string Newline { get; }
    public SyntaxLocator(string grammarProfile, int grammarVersion, long startByte, long lengthBytes,
        string preimageHash, string encoding, string newline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);
        ArgumentOutOfRangeException.ThrowIfLessThan(grammarVersion, 1);
        if (startByte < 0 || lengthBytes < 0 || startByte > long.MaxValue - lengthBytes)
            throw new ArgumentOutOfRangeException(nameof(startByte));
        if (preimageHash is null || preimageHash.Length != 64 || !preimageHash.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 preimage is required", nameof(preimageHash));
        if (newline is not ("lf" or "crlf" or "cr" or "mixed" or "none")) throw new ArgumentException("Invalid newline", nameof(newline));
        (GrammarProfile, GrammarVersion, StartByte, LengthBytes, PreimageHash, Encoding, Newline)
            = (grammarProfile, grammarVersion, startByte, lengthBytes, preimageHash.ToLowerInvariant(), encoding, newline);
    }
}

public sealed record SourceAddress
{
    public ArtifactAddress Artifact { get; }
    public TypedPath NodePath { get; }
    public string SemanticRole { get; }
    public string? Dimension { get; }
    public ChunkPosition? Chunk { get; }
    public TypedPath? ComponentPath { get; }
    public SyntaxLocator? Syntax { get; }
    public JsonTargetKind JsonTarget { get; }

    public SourceAddress(ArtifactAddress artifact, TypedPath nodePath, string semanticRole, string? dimension = null,
        ChunkPosition? chunk = null, TypedPath? componentPath = null, SyntaxLocator? syntax = null,
        JsonTargetKind jsonTarget = JsonTargetKind.Value)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(nodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticRole);
        if (!Enum.IsDefined(jsonTarget)) throw new ArgumentOutOfRangeException(nameof(jsonTarget));
        if (dimension is not null) ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        if (artifact.Kind is ArtifactKind.Region or ArtifactKind.Entities)
        {
            if (chunk is null || dimension is null) throw new ArgumentException("Region address requires dimension and chunk");
            var filename = artifact.RelativePath[(artifact.RelativePath.LastIndexOf('/') + 1)..].Split('.');
            if (filename.Length != 4 || filename[0] != "r" || filename[3] is not ("mca" or "mcr")
                || !int.TryParse(filename[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var regionX)
                || !int.TryParse(filename[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var regionZ)
                || regionX != chunk.RegionX || regionZ != chunk.RegionZ)
                throw new ArgumentException("Chunk does not match the exact region artifact", nameof(chunk));
        }
        else if (chunk is not null) throw new ArgumentException("Chunk requires a region/entity artifact", nameof(chunk));
        if (jsonTarget == JsonTargetKind.PropertyName && artifact.Kind is not (ArtifactKind.Json or ArtifactKind.Archive))
            throw new ArgumentException("Property-name target requires JSON", nameof(jsonTarget));
        if (artifact.Kind == ArtifactKind.Archive && artifact.ContainerEntry is null
            && (nodePath.Segments.Length != 0 || componentPath is not null || syntax is not null))
            throw new ArgumentException("An archive node requires an exact entry");
        (Artifact, NodePath, SemanticRole, Dimension, Chunk, ComponentPath, Syntax, JsonTarget)
            = (artifact, nodePath, semanticRole, dimension, chunk, componentPath, syntax, jsonTarget);
    }
}
