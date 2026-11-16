using Next.Domain;
using Next.Formats;
using Next.Minecraft;

namespace Next.Application;

internal static class WorldArchiveEdits
{
    internal static PreparedArtifactBytes Create(byte[] bytes, IReadOnlyList<PatchOperation> operations, IReadOnlyList<DiscoveredSource> sources, DecodeLimits limits)
    {
        using var archive = ZipArchiveValidation.OpenRead(bytes, limits);
        var replacements = new List<(string Entry, Hash256 Expected, byte[] Replacement)>(); long retainedBytes = 0;
        foreach (var group in operations.GroupBy(operation => operation.Targets.Where(address => address.Artifact.RelativePath == sources[0].Location.Artifact.RelativePath).Select(address => address.Artifact.ContainerEntry).Distinct().Single() ?? throw new EditRejectedException(EditFailure.Precondition, "ZIP operation requires an exact entry")))
        {
            var source = sources.Single(item => item.Location.Artifact.ContainerEntry == group.Key);
            var entry = archive.GetEntry(group.Key) ?? throw new EditRejectedException(EditFailure.Precondition, "ZIP source entry disappeared");
            var original = new byte[checked((int)entry.Length)]; using (var stream = entry.Open()) stream.ReadExactly(original);
            PreparedArtifactBytes transformed;
            if (source.Location.Role == WorldSourceRole.Function) transformed = WorldCommandEdits.Create(source.Location.Artifact, original, group.ToArray(), source, limits);
            else if (source.Location.Role == WorldSourceRole.Structure)
            {
                var parsed = WorldArtifactEdits.ReadNbt(original, limits); var expected = WorldArtifactEdits.Transform(parsed.Document, group.ToArray(), source, limits);
                var output = expected == parsed.Document ? original : CompressedNbt.WriteDocument(expected, parsed.Compression, limits);
                transformed = new(output, stream => { if (WorldArtifactEdits.ReadNbt(WorldArtifactEdits.Read(stream), limits).Document != expected) throw new InvalidDataException("ZIP NBT candidate projection differs"); });
            }
            else if (source.Location.Role == WorldSourceRole.Language && group.Any(operation => operation is KeyConversionPatch)) transformed = WorldLanguageEdits.Create(source.Location.Artifact, original, group.ToArray(), limits);
            else if (source.Location.Role is WorldSourceRole.PackJson or WorldSourceRole.PackMetadata or WorldSourceRole.Language) transformed = WorldJsonEdits.Create(original, group.ToArray(), limits);
            else throw new NotSupportedException("Unknown ZIP entry preparation profile");
            retainedBytes = checked(retainedBytes + transformed.Bytes.Length);
            if (retainedBytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "ZIP transformed entry buffers exceed aggregate budget");
            using (var verify = new MemoryStream(transformed.Bytes, writable: false)) transformed.Verify(verify);
            replacements.Add((group.Key, source.ContentHash!, transformed.Bytes));
        }
        var candidate = ZipArchiveValidation.ReplaceEntries(bytes, replacements, limits);
        return new(candidate, stream => ZipArchiveValidation.Validate(WorldArtifactEdits.Read(stream), limits));
    }
}
