namespace Next.Domain;

public sealed record LiteralTranslation(TextSlot Slot, SlotReply Reply);

public static class LiteralTranslations
{
    public static NbtDocument Apply(SourceAddress document, NbtDocument source, IEnumerable<LiteralTranslation> translations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translations);
        NbtDocumentScope.RequireDocument(document);
        var edits = new List<TypedEdit>();
        foreach (var translation in translations)
        {
            if (edits.Count >= 100_000) throw new ArgumentException("Too many translations", nameof(translations));
            ArgumentNullException.ThrowIfNull(translation);
            ArgumentNullException.ThrowIfNull(translation.Slot);
            var slot = translation.Slot;
            var address = slot.Address;
            NbtDocumentScope.RequireTarget(document, address);
            var status = slot.Validate(translation.Reply);
            if (status != SlotValidation.Valid) throw new EditRejectedException(EditFailure.Precondition, "Invalid slot reply: " + status);
            edits.Add(TypedEdit.Replace(address.NodePath, new NbtString(slot.SourceText), new NbtString(translation.Reply.Text)));
        }
        return TypedTreeEdits.Apply(source, edits);
    }

    public static bool Verify(SourceAddress document, NbtDocument source, NbtDocument candidate, IEnumerable<LiteralTranslation> translations)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Apply(document, source, translations) == candidate;
    }
}
