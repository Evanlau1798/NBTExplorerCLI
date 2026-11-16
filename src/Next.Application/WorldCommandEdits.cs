using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Text;

namespace Next.Application;

internal static class WorldCommandEdits
{
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, byte[] bytes, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits)
    {
        if (operations.Any(operation => operation is not LiteralPatch)) throw new NotSupportedException("Command preparation requires exact literal slots");
        var profile = MinecraftProfiles.ForPackMetadata(source.Pack ?? throw new NotSupportedException("Command pack profile is missing")) ?? throw new NotSupportedException("Command pack profile is unknown");
        CommandCandidate Bind(byte[] value)
        {
            var start = value.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0; var text = new UTF8Encoding(false, true).GetString(value.AsSpan(start));
            var grammar = profile.Features[ProfileFacet.CommandGrammar];
            return new(new(artifact, TypedPath.Root, "command.function", source.Location.Dimension,
                syntax: new(grammar.Schema, grammar.Version, start, value.Length - start, Hash256.Compute(value).Hex, start == 0 ? "utf8" : "utf8-bom", SyntaxLocator.DetectNewline(text))), text);
        }
        var before = Bind(bytes); var literal = operations.Cast<LiteralPatch>().Select(operation => operation.Translation).ToArray();
        var output = Rewrite(before, literal, profile, limits);
        return new(output, stream => Verify(before, Bind(WorldArtifactEdits.Read(stream)), literal, profile, limits));
    }
    internal static byte[] Rewrite(CommandCandidate source, IReadOnlyList<LiteralTranslation> translations, CapabilityProfile profile, DecodeLimits limits)
    {
        var encoding = new UTF8Encoding(false, true); var raw = encoding.GetBytes(source.Command);
        var bom = source.Address.Syntax is { Encoding: "utf8-bom", StartByte: 3 }; byte[] bytes = bom ? [0xef, 0xbb, 0xbf, .. raw] : raw;
        var original = CommandTextExtractor.Extract(source, profile, limits); var slots = original.Text.Occurrences.ToDictionary(item => item.Address);
        foreach (var item in translations)
            if (!slots.TryGetValue(item.Slot.Address, out var actual) || actual.Text != item.Slot.SourceText || item.Slot.Address.Syntax is null) throw new EditRejectedException(EditFailure.Precondition, "Command slot differs from its decoded source");
        var edits = new List<(SyntaxLocator Span, byte[] Bytes)>(); long replacementBytes = 0; var modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
        foreach (var group in translations.GroupBy(item => (item.Slot.Address.Syntax!.StartByte, item.Slot.Address.Syntax.LengthBytes, item.Slot.Address.Syntax.NodePath)))
        {
            var first = group.First(); var span = first.Slot.Address.Syntax!;
            if (group.All(item => item.Slot.SourceText == item.Reply.Text)) continue;
            byte[] replacement;
            if (first.Slot.Address.ComponentPath is not null)
            {
                if (group.Any(item => item.Slot.Address.ComponentPath is null)) throw new EditRejectedException(EditFailure.Precondition, "Command syntax layers conflict");
                var parsed = Snbt.ParseFragment(bytes, checked((int)span.StartByte), modern ? SnbtProfile.Java1215 : SnbtProfile.JavaLegacy, limits);
                if (parsed.Root.Span.LengthBytes != span.LengthBytes || parsed.Root.Value is not LogicalMinecraftLeaf { Value: NbtString text }) throw new EditRejectedException(EditFailure.Precondition, "Command embedded JSON span differs");
                var json = NativeJsonEdits.ReplaceStrings(NativeJsonStrings.EncodeJavaDocument(text.Value, limits), group.Select(item => (item.Slot.Address.ComponentPath!, item.Slot.SourceText, item.Reply.Text)), limits);
                replacement = Snbt.Write(new LogicalMinecraftLeaf(new NbtString(encoding.GetString(json))), modern ? SnbtProfile.Java1215 : SnbtProfile.JavaLegacy, limits);
            }
            else
            {
                if (group.Count() != 1) throw new EditRejectedException(EditFailure.Precondition, "Command slot scope overlaps");
                replacement = !modern && first.Slot.Role.StartsWith(source.Address.SemanticRole + ".text", StringComparison.Ordinal)
                    ? NativeJsonEdits.ReplaceStrings("\"\""u8.ToArray(), [(TypedPath.Root, "", first.Reply.Text)], limits)
                    : Snbt.Write(new LogicalMinecraftLeaf(new NbtString(first.Reply.Text)), modern ? SnbtProfile.Java1215 : SnbtProfile.JavaLegacy, limits);
            }
            if (replacement.Contains((byte)'\r') || replacement.Contains((byte)'\n')) throw new NotSupportedException("This command profile cannot encode a literal line break inside a syntax token");
            replacementBytes = checked(replacementBytes + replacement.Length);
            if (replacementBytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Command replacements exceed decode budget");
            edits.Add((span, replacement));
        }
        long length = bytes.Length;
        foreach (var edit in edits) length = checked(length - edit.Span.LengthBytes + edit.Bytes.Length);
        if (length > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Command candidate exceeds decode budget");
        var output = new byte[checked((int)length)]; var from = 0; var to = 0;
        foreach (var edit in edits.OrderBy(edit => edit.Span.StartByte))
        {
            var start = checked((int)edit.Span.StartByte); var end = checked(start + (int)edit.Span.LengthBytes);
            if (start < from || start > bytes.Length || end > bytes.Length) throw new EditRejectedException(EditFailure.Precondition, "Command target spans overlap or exceed their source");
            bytes.AsSpan(from, start - from).CopyTo(output.AsSpan(to)); to += start - from; edit.Bytes.CopyTo(output.AsSpan(to)); to += edit.Bytes.Length; from = end;
        }
        bytes.AsSpan(from).CopyTo(output.AsSpan(to));
        var script = encoding.GetString(output.AsSpan(bom ? 3 : 0)); var root = source.Address; var syntax = root.Syntax;
        var address = new SourceAddress(root.Artifact, root.NodePath, root.SemanticRole, root.Dimension, root.Chunk,
            syntax: syntax is null ? null : new(syntax.GrammarProfile, syntax.GrammarVersion, bom ? 3 : 0, output.Length - (bom ? 3 : 0), Hash256.Compute(output).Hex, syntax.Encoding, SyntaxLocator.DetectNewline(script), syntax.NodePath));
        Verify(source, new(address, script), translations, profile, limits); return output;
    }
    private static void Verify(CommandCandidate before, CommandCandidate after, IReadOnlyList<LiteralTranslation> translations, CapabilityProfile profile, DecodeLimits limits)
    {
        var original = CommandTextExtractor.Extract(before, profile, limits); var actual = CommandTextExtractor.Extract(after, profile, limits);
        var expected = original.Text.Occurrences.ToDictionary(item => SourceIdentity.Occurrence(item.Address).Digest, item => item.Text);
        foreach (var item in translations) expected[SourceIdentity.Occurrence(item.Slot.Address).Digest] = item.Reply.Text;
        static string Id(SourceAddress address) => SourceIdentity.Occurrence(address).Digest.Hex;
        var result = actual.Text.Occurrences.ToDictionary(item => SourceIdentity.Occurrence(item.Address).Digest, item => item.Text);
        if (result.Count != expected.Count || expected.Any(item => !result.TryGetValue(item.Key, out var value) || value != item.Value))
            throw new EditRejectedException(EditFailure.Precondition, "Command candidate text projection differs");
        if (!original.Text.TranslationKeys.Select(item => (Id(item.Address), item.Key)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.Text.TranslationKeys.Select(item => (Id(item.Address), item.Key)).OrderBy(item => item.Item1, StringComparer.Ordinal))
            || !original.References.Select(item => (Id(item.Address), item.Name, item.Negated)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.References.Select(item => (Id(item.Address), item.Name, item.Negated)).OrderBy(item => item.Item1, StringComparer.Ordinal))
            || !original.Text.Diagnostics.Select(item => (Id(item.Address), item.Code)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.Text.Diagnostics.Select(item => (Id(item.Address), item.Code)).OrderBy(item => item.Item1, StringComparer.Ordinal)))
            throw new EditRejectedException(EditFailure.Precondition, "Command candidate protected references or diagnostics differ");
    }
}
