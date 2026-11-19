using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal static class WorldArchiveEdits
{
    internal static PreparedArtifactBytes Create(byte[] bytes, IReadOnlyList<PatchOperation> operations, IReadOnlyList<DiscoveredSource> sources, DecodeLimits limits,
        IReadOnlySet<(string Storage, string Path)>? storageDisplayLists = null)
    {
        using var archive = ZipArchiveValidation.OpenRead(bytes, limits);
        var replacements = new List<(string Entry, Hash256 Expected, byte[] Replacement)>(); var additions = new List<(string Entry, byte[] Contents)>(); long retainedBytes = 0;
        var targets = operations.SelectMany(operation => operation.Targets).Where(address => address.Artifact.RelativePath == sources[0].Location.Artifact.RelativePath)
            .Select(address => address.Artifact.ContainerEntry ?? throw new EditRejectedException(EditFailure.Precondition, "ZIP operation requires an exact entry")).Distinct(StringComparer.Ordinal);
        foreach (var name in targets)
        {
            using var entryTrace = WorkDiagnostics.Start("zip.entry.read");
            entryTrace?.SetTag("entry", name);
            var target = new ArtifactAddress(sources[0].Location.Artifact.WorldId, sources[0].Location.Artifact.RelativePath, ArtifactKind.Archive, name);
            var group = operations.Where(operation => operation.Targets.Any(address => address.Artifact == target)).ToArray();
            var source = sources.Single(item => item.Location.Artifact.ContainerEntry == name);
            var entry = archive.GetEntry(name); var missing = source.ContentHash is null;
            if (missing && entry is not null || !missing && entry is null) throw new EditRejectedException(EditFailure.Precondition, "ZIP entry existence differs from its source binding");
            if (missing && source.Location.Role != WorldSourceRole.Language) throw new NotSupportedException("New ZIP entries require declared language dependencies");
            var original = entry is null ? "{}"u8.ToArray() : new byte[checked((int)entry.Length)];
            if (entry is not null) { using var stream = entry.Open(); stream.ReadExactly(original); }
            entryTrace?.SetTag("bytes", entry is null ? 0L : original.LongLength);
            PreparedArtifactBytes transformed;
            if (source.Location.Role == WorldSourceRole.Function) transformed = WorldCommandEdits.Create(source.Location.Artifact, original, group.ToArray(), source, limits, storageDisplayLists);
            else if (source.Location.Role == WorldSourceRole.Structure)
            {
                var parsed = WorldArtifactEdits.ReadNbt(original, limits); var expected = WorldArtifactEdits.Transform(parsed.Document, group.ToArray(), source, limits);
                var output = expected == parsed.Document ? original : CompressedNbt.WriteDocument(expected, parsed.Compression, limits);
                transformed = new(output, stream => { if (WorldArtifactEdits.ReadNbt(WorldArtifactEdits.Read(stream), limits).Document != expected) throw new InvalidDataException("ZIP NBT candidate projection differs"); });
            }
            else if (source.Location.Role == WorldSourceRole.Language && group.Any(operation => operation is KeyConversionPatch)) transformed = WorldLanguageEdits.Create(source.Location.Artifact, original, group.ToArray(), limits);
            else if (source.Location.Role is WorldSourceRole.PackJson or WorldSourceRole.PackMetadata or WorldSourceRole.Language) transformed = WorldJsonEdits.Create(original, group.ToArray(), limits, source);
            else throw new NotSupportedException("Unknown ZIP entry preparation profile");
            retainedBytes = checked(retainedBytes + transformed.Bytes.Length);
            if (retainedBytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "ZIP transformed entry buffers exceed aggregate budget");
            using (WorkDiagnostics.Start("zip.entry.projection", phase: "projection"))
                using (var verify = new MemoryStream(transformed.Bytes, writable: false)) transformed.Verify(verify);
            if (missing) additions.Add((name, transformed.Bytes)); else replacements.Add((name, source.ContentHash!, transformed.Bytes));
        }
        var candidate = ZipArchiveValidation.ReplaceEntries(bytes, replacements, limits);
        if (additions.Count != 0) candidate = ZipArchiveValidation.AddEntries(candidate, additions, limits);
        return new(candidate, stream => ZipArchiveValidation.Validate(WorldArtifactEdits.Read(stream), limits));
    }
}
