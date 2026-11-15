namespace Next.Domain;

internal static class OperationEncoding
{
    public static void Write(ContractWriter writer, PatchOperation operation)
    {
        writer.Text(operation.Id);
        writer.Byte((byte)operation.Kind);
        switch (operation)
        {
            case TypedPatch typed:
                writer.Address(typed.Address);
                var edit = typed.Edit;
                writer.Byte((byte)edit.Action);
                writer.Path(edit.Path);
                writer.Value(edit.Expected);
                writer.Byte(edit.Replacement is null ? (byte)0 : (byte)1);
                if (edit.Replacement is not null) writer.Value(edit.Replacement);
                writer.Byte(edit.Child is null ? (byte)0 : (byte)1);
                if (edit.Child is not null) writer.Segment(edit.Child);
                writer.Optional(edit.NewName);
                break;
            case LiteralPatch literal:
                Slot(writer, literal.Translation.Slot, literal.Translation.Reply);
                break;
            case KeyConversionPatch key:
                var conversion = key.Conversion;
                writer.Address(conversion.Address);
                writer.Value(conversion.Expected);
                writer.Byte((byte)conversion.Rule);
                writer.Text(conversion.Profile);
                writer.Int(conversion.ProfileVersion);
                Slot(writer, conversion.Slot, conversion.Reply);
                var dependency = conversion.Dependency;
                writer.Artifact(dependency.LanguageArtifact);
                writer.Byte(dependency.PackArtifact is null ? (byte)0 : (byte)1);
                if (dependency.PackArtifact is not null) writer.Artifact(dependency.PackArtifact);
                writer.Text(dependency.Locale);
                writer.Text(dependency.Key);
                writer.Text(dependency.Text);
                break;
            case ReferencePatch reference:
                var synchronization = reference.Synchronization;
                writer.Identity(synchronization.GroupId);
                writer.Long(synchronization.GroupRevision);
                writer.Hash(synchronization.MemberDigest);
                writer.Byte((byte)synchronization.Completeness);
                writer.Int(synchronization.Members.Length);
                foreach (var member in synchronization.Members)
                {
                    writer.Address(member.Address);
                    writer.Text(member.ExpectedText);
                    writer.Text(member.ReplacementText);
                    writer.Text(member.GrammarProfile);
                    writer.Int(member.GrammarVersion);
                }
                break;
            case RestorePatch restore:
                writer.Hash(restore.Restore.VerifiedManifestHash);
                writer.Int(restore.Restore.AuthorizedArtifacts.Length);
                foreach (var artifact in restore.Restore.AuthorizedArtifacts.OrderBy(item => item.Artifact.RelativePath, StringComparer.Ordinal))
                {
                    writer.Artifact(artifact.Artifact);
                    writer.Image(restore.Restore.CurrentImages[artifact.Artifact]);
                    writer.Image(artifact.Image);
                }
                break;
            default: throw new ArgumentException("Unknown operation kind", nameof(operation));
        }
    }
    private static void Slot(ContractWriter writer, TextSlot slot, SlotReply reply)
    {
        writer.Address(slot.Address);
        writer.Identity(slot.Id);
        writer.Text(slot.SourceText);
        writer.Hash(slot.SourceTextHash);
        writer.Text(slot.Context);
        writer.Text(slot.SourceLocale);
        writer.Text(slot.TargetLocale);
        writer.Byte(slot.AllowedEmpty ? (byte)1 : (byte)0);
        var tokens = slot.Placeholders.Tokens.Order(StringComparer.Ordinal).ToArray();
        writer.Int(tokens.Length);
        foreach (var token in tokens) writer.Text(token);
        writer.Identity(reply.SlotId);
        writer.Hash(reply.SourceTextHash);
        writer.Text(reply.TargetLocale);
        writer.Text(reply.Text);
    }
}
