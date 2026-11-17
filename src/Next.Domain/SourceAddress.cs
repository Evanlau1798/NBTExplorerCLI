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
        var text = path.AsSpan(); var start = 0;
        var invalid = entry && text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':';
        for (var index = 0; !invalid && index <= text.Length; index++)
        {
            if (index == text.Length || text[index] == '/')
            {
                var part = text[start..index];
                invalid = part.IsEmpty || part.SequenceEqual(".".AsSpan()) || part.SequenceEqual("..".AsSpan())
                    || !entry && part[^1] is '.' or ' ';
                start = index + 1;
            }
            else
            {
                var unit = text[index];
                invalid = unit is '\\' or '\0' || !entry && (unit < 32 || unit is '<' or '>' or ':' or '"' or '|' or '?' or '*');
            }
        }
        if (invalid)
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
    public static string DetectNewline(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var crlf = false; var cr = false; var lf = false;
        for (var index = 0; index < source.Length; index++)
            if (source[index] == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n') { crlf = true; index++; } else cr = true;
            }
            else if (source[index] == '\n') lf = true;
        return (crlf ? 1 : 0) + (cr ? 1 : 0) + (lf ? 1 : 0) > 1 ? "mixed" : crlf ? "crlf" : cr ? "cr" : lf ? "lf" : "none";
    }
    public string GrammarProfile { get; }
    public int GrammarVersion { get; }
    public long StartByte { get; }
    public long LengthBytes { get; }
    public string PreimageHash { get; }
    public string Encoding { get; }
    public string Newline { get; }
    public TypedPath? NodePath { get; }
    public SyntaxLocator(string grammarProfile, int grammarVersion, long startByte, long lengthBytes,
        string preimageHash, string encoding, string newline, TypedPath? nodePath = null)
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
        NodePath = nodePath;
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
