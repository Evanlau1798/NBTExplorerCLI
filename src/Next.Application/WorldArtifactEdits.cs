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
        if (artifact.Kind == ArtifactKind.Json) return WorldJsonEdits.Create(bytes, operations, limits);
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
        edits.AddRange(operations.OfType<KeyConversionPatch>().Where(operation => operation.Conversion.Address.Artifact == source.Location.Artifact && operation.Conversion.Rule != KeyConversionRule.EncodedTextToTranslate).Select(operation =>
            TypedEdit.Replace(operation.Conversion.Address.NodePath, operation.Conversion.Expected, WorldKeyEdits.Replacement(operation.Conversion))));
        foreach (var group in operations.OfType<KeyConversionPatch>().Where(operation => operation.Conversion.Rule == KeyConversionRule.EncodedTextToTranslate).GroupBy(operation => operation.Conversion.Address.NodePath))
        {
            if (NbtLocator.Resolve(document.Root, group.Key).Value is not NbtString text) throw new EditRejectedException(EditFailure.Precondition, "Encoded component source is not a string");
            edits.Add(TypedEdit.Replace(group.Key, text, new NbtString(WorldKeyEdits.RewriteJson(text.Value, group.Select(operation => operation.Conversion).ToArray(), limits))));
        }
        var literal = operations.OfType<LiteralPatch>().Select(operation => operation.Translation).ToArray();
        foreach (var group in literal.Where(item => item.Slot.Address.Syntax is not null).GroupBy(item => item.Slot.Address.NodePath))
        {
            var pending = NbtTextExtractor.Extract(source.Location, document, source.Chunk, limits).PendingCommands.SingleOrDefault(item => item.Address.NodePath.Equals(group.Key))
                ?? throw new EditRejectedException(EditFailure.Precondition, "NBT syntax target is not an actual command field");
            var changed = WorldCommandEdits.Rewrite(pending, group.ToArray(), profile, limits);
            edits.Add(TypedEdit.Replace(group.Key, new NbtString(pending.Command), new NbtString(new UTF8Encoding(false, true).GetString(changed))));
        }
        foreach (var item in literal.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is null)) edits.Add(TypedEdit.Replace(item.Slot.Address.NodePath, new NbtString(item.Slot.SourceText), new NbtString(item.Reply.Text)));
        foreach (var group in literal.Where(item => item.Slot.Address.Syntax is null && item.Slot.Address.ComponentPath is not null).GroupBy(item => item.Slot.Address.NodePath))
        {
            if (NbtLocator.Resolve(document.Root, group.Key).Value is not NbtString text) throw new EditRejectedException(EditFailure.Precondition, "Embedded JSON source is not a string");
            edits.Add(TypedEdit.Replace(group.Key, text, new NbtString(WorldJsonEdits.RewriteJson(text.Value, group.ToArray(), limits))));
        }
        var expected = TypedTreeEdits.Apply(document, edits);
        if (NbtTextExtractor.Profile(source.Location, expected).Id != profile.Id) throw new NotSupportedException("Data version migration requires its explicit profile conversion");
        return expected;
    }
    private static PreparedArtifactBytes Region(byte[] bytes, IReadOnlyList<PatchOperation> operations, IReadOnlyList<DiscoveredSource> sources, DecodeLimits limits)
    {
        var original = RegionFile.Read(bytes); var output = bytes; var expected = new Dictionary<int, (RegionChunk Descriptor, Hash256 Payload)>();
        foreach (var group in operations.GroupBy(operation => operation.Targets.Where(address => address.Artifact == sources[0].Location.Artifact && address.Chunk is not null).Select(address => address.Chunk).Distinct().Single() ?? throw new EditRejectedException(EditFailure.Precondition, "Region operation requires an exact chunk")))
        {
            var index = group.Key.LocalZ * 32 + group.Key.LocalX;
            var source = sources.Single(item => item.Chunk == group.Key); var before = original.ReadChunk(index, limits).Document;
            var after = Transform(before, group.ToArray(), source, limits);
            output = RegionFile.Read(output).WithChunk(index, after, (NbtCompression)original.Chunks[index].CodecId, original.Chunks[index].Timestamp, limits);
            var prepared = RegionFile.Read(output);
            if (prepared.ReadChunk(index, limits).Document != after) throw new InvalidDataException("Region candidate differs from its authorized tree");
            expected.Add(index, (prepared.Chunks[index], Hash256.Compute(prepared.CompressedPayload(index))));
        }
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
