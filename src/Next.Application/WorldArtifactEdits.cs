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
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, byte[] bytes, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits)
    {
        if (artifact.Kind == ArtifactKind.Nbt) return Nbt(bytes, operations, source, limits);
        if (artifact.Kind == ArtifactKind.Json)
        {
            if (operations.Any(operation => operation is not LiteralPatch || operation.Targets.Any(address => address.Syntax is not null || address.ComponentPath is not null || address.JsonTarget != JsonTargetKind.Value))) throw new NotSupportedException("Encoded JSON operation preparation is not implemented");
            var output = NativeJsonEdits.ReplaceStrings(bytes, operations.Cast<LiteralPatch>().Select(operation => (operation.Translation.Slot.Address.NodePath, operation.Translation.Slot.SourceText, operation.Translation.Reply.Text)), limits);
            return new(output, stream => { using var parsed = BoundedJson.ReadDecoded(Read(stream), limits); });
        }
        if (artifact.Kind == ArtifactKind.McFunction) return WorldCommandEdits.Create(artifact, bytes, operations, source, limits);
        throw new NotSupportedException("Artifact format preparation is not implemented");
    }
    private static PreparedArtifactBytes Nbt(byte[] bytes, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits)
    {
        var parsed = ReadNbt(bytes, limits); var profile = NbtTextExtractor.Profile(source.Location, parsed.Document);
        var edits = operations.OfType<TypedPatch>().Select(operation => operation.Edit).ToList();
        var literal = operations.OfType<LiteralPatch>().Select(operation => operation.Translation).ToArray();
        if (literal.Any(item => item.Slot.Address.Syntax is not null)) throw new NotSupportedException("NBT command preparation is not implemented");
        foreach (var item in literal.Where(item => item.Slot.Address.ComponentPath is null)) edits.Add(TypedEdit.Replace(item.Slot.Address.NodePath, new NbtString(item.Slot.SourceText), new NbtString(item.Reply.Text)));
        foreach (var group in literal.Where(item => item.Slot.Address.ComponentPath is not null).GroupBy(item => item.Slot.Address.NodePath))
        {
            if (NbtLocator.Resolve(parsed.Document.Root, group.Key).Value is not NbtString text) throw new EditRejectedException(EditFailure.Precondition, "Embedded JSON source is not a string");
            var encoded = NativeJsonStrings.EncodeJavaDocument(text.Value, limits);
            var changed = NativeJsonEdits.ReplaceStrings(encoded, group.Select(item => (item.Slot.Address.ComponentPath!, item.Slot.SourceText, item.Reply.Text)), limits);
            edits.Add(TypedEdit.Replace(group.Key, text, new NbtString(changed.AsSpan().SequenceEqual(encoded) ? text.Value : new UTF8Encoding(false, true).GetString(changed))));
        }
        var expected = TypedTreeEdits.Apply(parsed.Document, edits);
        if (NbtTextExtractor.Profile(source.Location, expected).Id != profile.Id) throw new NotSupportedException("Data version migration requires its explicit profile conversion");
        var output = expected == parsed.Document ? bytes : CompressedNbt.WriteDocument(expected, parsed.Compression, limits);
        return new(output, stream => { if (CompressedNbt.ReadDocument(stream, parsed.Compression, limits).Document != expected) throw new InvalidDataException("Persisted NBT candidate differs from its authorized tree"); });
    }
    internal static byte[] Read(Stream stream)
    {
        if (stream.Length > 67_108_864) throw new IOException("Persisted candidate exceeds artifact budget");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}
