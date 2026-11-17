using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Text;

namespace Next.Application;

internal sealed record PreparedArtifactBytes(byte[] Bytes, Action<Stream> Verify);

internal static class WorldArtifactEdits
{
    internal static (NbtDocument Document, NbtCompression Compression) ReadNbt(byte[] bytes, DecodeLimits limits)
    {
        var codec = bytes.Length == 0 || bytes[0] == (byte)NbtType.Compound ? NbtCompression.Raw : bytes[0] == 0x1f ? NbtCompression.GZip : NbtCompression.Zlib;
        return (CompressedNbt.ReadDocument(bytes, codec, limits).Document, codec);
    }
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, byte[] bytes, IReadOnlyList<PatchOperation> operations, IReadOnlyList<DiscoveredSource> sources, DecodeLimits limits)
    {
        var source = sources.Single(item => item.Location.Artifact == artifact && item.Chunk is null);
        if (source.Location.Role == WorldSourceRole.Language && operations.Any(operation => operation is KeyConversionPatch)) return WorldLanguageEdits.Create(artifact, bytes, operations, limits);
        if (artifact.Kind == ArtifactKind.Nbt) return Nbt(bytes, operations, source, limits);
        if (artifact.Kind == ArtifactKind.Json) return WorldJsonEdits.Create(bytes, operations, limits, artifact);
        if (artifact.Kind == ArtifactKind.McFunction) return WorldCommandEdits.Create(artifact, bytes, operations, source, limits);
        if (artifact.Kind is ArtifactKind.Region or ArtifactKind.Entities) return Region(bytes, operations, sources, limits);
        if (artifact.Kind == ArtifactKind.Archive) return WorldArchiveEdits.Create(bytes, operations, sources, limits);
        throw new NotSupportedException("Artifact format preparation is not implemented");
    }
    private static PreparedArtifactBytes Nbt(byte[] bytes, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits)
    {
        var parsed = ReadNbt(bytes, limits); var expected = Transform(parsed.Document, operations, source, limits);
        var output = expected == parsed.Document ? bytes : CompressedNbt.WriteDocument(expected, parsed.Compression, limits);
        return new(output, stream => { if (CompressedNbt.ReadDocument(stream, parsed.Compression, limits).Document != expected) throw new InvalidDataException("Persisted NBT candidate differs from its authorized tree"); });
    }
    internal static NbtDocument Transform(NbtDocument document, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits)
    {
        var profile = NbtTextExtractor.Profile(source.Location, document);
        var edits = operations.OfType<TypedPatch>().Select(operation => operation.Edit).ToList();
        var conversions = operations.OfType<KeyConversionPatch>().Select(operation => operation.Conversion).ToArray();
        var keyed = WorldKeyEdits.ApplyNative(document, conversions.Where(conversion => conversion.Address.Artifact == source.Location.Artifact && conversion.Rule != KeyConversionRule.EncodedTextToTranslate).ToArray());
        var literal = operations.OfType<LiteralPatch>().Select(operation => operation.Translation)
            .Concat(WorldReferenceMembers.Literals(operations, source.Location.Artifact, source.Chunk)).ToArray();
        var encodedPaths = conversions.Where(conversion => conversion.Rule == KeyConversionRule.EncodedTextToTranslate && conversion.Address.Syntax is null).Select(conversion => conversion.Address.NodePath)
            .Concat(literal.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is not null).Select(item => item.Slot.Address.NodePath)).Distinct();
        foreach (var path in encodedPaths)
        {
            if (NbtLocator.Resolve(document.Root, path).Value is not NbtString text) throw new EditRejectedException(EditFailure.Precondition, "Encoded component source is not a string");
            var changed = WorldKeyEdits.RewriteJson(text.Value, conversions.Where(conversion => conversion.Rule == KeyConversionRule.EncodedTextToTranslate && conversion.Address.Syntax is null && conversion.Address.NodePath.Equals(path)).ToArray(), limits,
                literal.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is not null && item.Slot.Address.NodePath.Equals(path)).ToArray());
            edits.Add(TypedEdit.Replace(path, text, new NbtString(changed)));
        }
        var commandPaths = literal.Where(item => item.Slot.Address.Syntax is not null).Select(item => item.Slot.Address.NodePath)
            .Concat(conversions.Where(conversion => conversion.Address.Syntax is not null).Select(conversion => conversion.Address.NodePath)).Distinct();
        foreach (var path in commandPaths)
        {
            var pending = NbtTextExtractor.Extract(source.Location, document, source.Chunk, limits).PendingCommands.SingleOrDefault(item => item.Address.NodePath.Equals(path))
                ?? throw new EditRejectedException(EditFailure.Precondition, "NBT syntax target is not an actual command field");
            var changed = WorldCommandEdits.Rewrite(pending, literal.Where(item => item.Slot.Address.Syntax is not null && item.Slot.Address.NodePath.Equals(path)).ToArray(), profile, limits,
                conversions.Where(conversion => conversion.Address.Syntax is not null && conversion.Address.NodePath.Equals(path)).ToArray());
            edits.Add(TypedEdit.Replace(path, new NbtString(pending.Command), new NbtString(new UTF8Encoding(false, true).GetString(changed))));
        }
        foreach (var item in literal.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is null)) edits.Add(TypedEdit.Replace(item.Slot.Address.NodePath, new NbtString(item.Slot.SourceText), new NbtString(item.Reply.Text)));
        var expected = TypedTreeEdits.Apply(keyed, edits);
        if (NbtTextExtractor.Profile(source.Location, expected).Id != profile.Id) throw new NotSupportedException("Data version migration requires its explicit profile conversion");
        return expected;
    }
    private static PreparedArtifactBytes Region(byte[] bytes, IReadOnlyList<PatchOperation> operations, IReadOnlyList<DiscoveredSource> sources, DecodeLimits limits)
    {
        var original = RegionFile.Read(bytes);
        var artifact = sources[0].Location.Artifact;
        static int Index(ChunkPosition chunk) => chunk.LocalZ * 32 + chunk.LocalX;
        var groups = operations.SelectMany(operation => operation.Targets
            .Where(address => address.Artifact == artifact && address.Chunk is not null)
            .Select(address => address.Chunk!).Distinct().Select(chunk => (Chunk: chunk, Operation: operation)))
            .GroupBy(item => item.Chunk).ToArray();
        var byChunk = groups.ToDictionary(group => Index(group.Key));
        var bySource = sources.Where(source => source.Chunk is not null).ToDictionary(source => source.Chunk!);
        var (output, expected) = original.WithEditedChunks(groups.Select(group => Index(group.Key)), (index, before) =>
        {
            var group = byChunk[index];
            return Transform(before, group.Select(item => item.Operation).ToArray(), bySource[group.Key], limits);
        }, limits);
        return new(output, stream =>
        {
            var candidate = RegionFile.Read(Read(stream));
            for (var index = 0; index < 1024; index++)
                if (expected.TryGetValue(index, out var verified))
                {
                    if (candidate.Chunks[index] != verified.Descriptor || Hash256.Compute(candidate.CompressedPayload(index)) != verified.Payload) throw new InvalidDataException("Region candidate differs from its verified chunk image");
                }
                else if (candidate.Chunks[index] != original.Chunks[index] || original.Chunks[index].Present && !candidate.CompressedPayload(index).SequenceEqual(original.CompressedPayload(index))) throw new InvalidDataException("Region changed an unselected chunk");
        });
    }
    internal static byte[] Read(Stream stream)
    {
        if (stream.Length > 67_108_864) throw new IOException("Persisted candidate exceeds artifact budget");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}
