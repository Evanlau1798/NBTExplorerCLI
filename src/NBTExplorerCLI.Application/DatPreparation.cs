using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

public static class DatPreparation
{
    public static IArtifactCandidate Prepare(IArtifactStorage storage, SourceAddress document, NbtCompression compression,
        ArtifactImage expectedPreimage, IReadOnlyList<PatchOperation> operations, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(storage); ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(expectedPreimage); ArgumentNullException.ThrowIfNull(operations);
        limits ??= DecodeLimits.Default; limits.Validate();
        if (document.Artifact.Kind != ArtifactKind.Nbt || document.NodePath.Segments.Length != 0
            || document.ComponentPath is not null || document.Syntax is not null || document.JsonTarget != JsonTargetKind.Value)
            throw new ArgumentException("Requires an exact standalone DAT document", nameof(document));
        if (operations.Count is 0 or > 100_000 || operations.Any(operation => operation is null)
            || operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count() != operations.Count)
            throw new ArgumentException("Requires bounded, unique explicit operations", nameof(operations));
        var bytes = storage.Read(document.Artifact);
        if (ArtifactImage.Present(Hash256.Compute(bytes)) != expectedPreimage) throw new ArtifactConflictException("DAT preimage changed");
        var source = CompressedNbt.ReadDocument(bytes, compression, limits).Document;
        var edits = new List<TypedEdit>(operations.Count);
        foreach (var operation in operations)
        {
            if (operation.Targets.Any(address => address.Artifact != document.Artifact || address.Dimension != document.Dimension || address.Chunk != document.Chunk))
                throw new ArgumentException("DAT operation target differs from its document", nameof(operations));
            if (operation is TypedPatch typed) edits.Add(typed.Edit);
            else if (operation is LiteralPatch literal)
            {
                _ = LiteralTranslations.Apply(document, source, [literal.Translation]);
                edits.Add(TypedEdit.Replace(literal.Translation.Slot.Address.NodePath,
                    new NbtString(literal.Translation.Slot.SourceText), new NbtString(literal.Translation.Reply.Text)));
            }
            else throw new NotSupportedException("DAT slice supports only explicit TypedEdit and LiteralTranslation");
        }
        var expected = TypedTreeEdits.Apply(source, edits);
        var encoded = expected == source ? bytes : CompressedNbt.WriteDocument(expected, compression, limits);
        var candidate = storage.Stage(document.Artifact, expectedPreimage, encoded);
        try
        {
            candidate.Flush();
            candidate.Verify(stream =>
            {
                var actual = CompressedNbt.ReadDocument(stream, compression, limits).Document;
                if (!TypedTreeEdits.Verify(source, actual, edits)) throw new InvalidDataException("DAT candidate changed outside authorized operations");
            });
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
}
