using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using System.Text;

namespace NBTExplorerCLI.Application;

internal static class WorldCommandEdits
{
    internal static PreparedArtifactBytes Create(ArtifactAddress artifact, byte[] bytes, IReadOnlyList<PatchOperation> operations, DiscoveredSource source, DecodeLimits limits,
        IReadOnlySet<(string Storage, string Path)>? storageDisplayLists = null)
    {
        if (operations.Any(operation => operation is not (LiteralPatch or KeyConversionPatch or ReferencePatch))) throw new NotSupportedException("Command preparation requires explicit semantic operations");
        var profile = MinecraftProfiles.ForPackMetadata(source.Pack ?? throw new NotSupportedException("Command pack profile is missing")) ?? throw new NotSupportedException("Command pack profile is unknown");
        var before = BindFunction(artifact, bytes, profile, source.Location.Dimension); var literal = operations.OfType<LiteralPatch>().Select(operation => operation.Translation).Concat(WorldReferenceMembers.Literals(operations, artifact)).ToArray();
        var conversions = operations.OfType<KeyConversionPatch>().Select(operation => operation.Conversion).ToArray();
        var output = Rewrite(before, literal, profile, limits, conversions, storageDisplayLists);
        return new(output, stream => Verify(before, BindFunction(artifact, WorldArtifactEdits.Read(stream), profile, source.Location.Dimension), literal, profile, limits, conversions, storageDisplayLists));
    }
    internal static CommandCandidate BindFunction(ArtifactAddress artifact, byte[] value, CapabilityProfile profile, string? dimension)
    {
        var start = value.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0; var text = new UTF8Encoding(false, true).GetString(value.AsSpan(start));
        var grammar = profile.Features[ProfileFacet.CommandGrammar];
        return new(new(artifact, TypedPath.Root, "command.function", dimension,
            syntax: new(grammar.Schema, grammar.Version, start, value.Length - start, Hash256.Compute(value).Hex, start == 0 ? "utf8" : "utf8-bom", SyntaxLocator.DetectNewline(text))), text);
    }
    internal static byte[] Rewrite(CommandCandidate source, IReadOnlyList<LiteralTranslation> translations, CapabilityProfile profile, DecodeLimits limits, IReadOnlyList<TranslationKeyConversion>? conversions = null,
        IReadOnlySet<(string Storage, string Path)>? storageDisplayLists = null)
    {
        conversions ??= [];
        var encoding = new UTF8Encoding(false, true); var raw = encoding.GetBytes(source.Command);
        var bom = source.Address.Syntax is { Encoding: "utf8-bom", StartByte: 3 }; byte[] bytes = bom ? [0xef, 0xbb, 0xbf, .. raw] : raw;
        var roots = new HashSet<SourceAddress>();
        var referenceTargets = new List<CommandSelectorReference>();
        var references = translations.Any(item => item.Slot.Role == "reference.selector.name");
        var original = CommandTextExtractor.Extract(source, profile, limits, componentRoot: conversions.Count == 0 ? null : root => roots.Add(root), selectorReference: references ? referenceTargets.Add : null,
            storageDisplayLists: storageDisplayLists);
        var slots = original.Text.Occurrences.ToDictionary(item => item.Address);
        foreach (var binding in referenceTargets.Where(binding => binding.Direct))
        {
            var reference = binding.Reference;
            slots.Add(reference.Address, new(reference.Address, reference.Name, reference.Address.SemanticRole, true));
        }
        foreach (var item in translations)
            if (!slots.TryGetValue(item.Slot.Address, out var actual) || actual.Text != item.Slot.SourceText || item.Slot.Address.Syntax is null) throw new EditRejectedException(EditFailure.Precondition, "Command slot differs from its decoded source");
        foreach (var conversion in conversions)
            if (!roots.Contains(conversion.Address) || !slots.TryGetValue(conversion.Slot.Address, out var actual) || actual.Text != conversion.Slot.SourceText
                || conversion.Profile != profile.Id || conversion.ProfileVersion != profile.Version) throw new EditRejectedException(EditFailure.Precondition, "Command key target differs from its exact component and slot profile");
        var keyedEncoded = conversions.Where(item => item.Address.ComponentPath is not null).Select(item => SpanKey(item.Address)).ToHashSet();
        var edits = new List<(SyntaxLocator Span, byte[] Bytes)>(); long replacementBytes = 0; var modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
        var grammar = MinecraftProfiles.SnbtGrammar(profile);
        foreach (var group in translations.GroupBy(item => SpanKey(item.Slot.Address)))
        {
            if (keyedEncoded.Contains(group.Key)) continue;
            var first = group.First(); var span = first.Slot.Address.Syntax!;
            if (group.All(item => item.Slot.SourceText == item.Reply.Text)) continue;
            byte[] replacement;
            if (profile == MinecraftProfiles.Java181 && first.Slot.Address.ComponentPath is not null
                && span.NodePath?.Segments.LastOrDefault()?.Key is "tellraw.text" or "title.text")
            {
                var component = encoding.GetString(bytes.AsSpan(checked((int)span.StartByte), checked((int)span.LengthBytes)));
                replacement = encoding.GetBytes(WorldJsonEdits.RewriteJson(ClassicTextJson.Normalize(component, limits), group.ToArray(), limits));
            }
            else if (first.Slot.Address.ComponentPath is not null)
            {
                if (group.Any(item => item.Slot.Address.ComponentPath is null)) throw new EditRejectedException(EditFailure.Precondition, "Command syntax layers conflict");
                var parsed = Snbt.ParseFragment(bytes, checked((int)span.StartByte), grammar, limits);
                if (parsed.Root.Span.LengthBytes != span.LengthBytes || parsed.Root.Value is not LogicalMinecraftLeaf { Value: NbtString text }) throw new EditRejectedException(EditFailure.Precondition, "Command embedded JSON span differs");
                var originalText = profile == MinecraftProfiles.Java181 ? ClassicTextJson.Normalize(text.Value, limits, storedComponent: true) : text.Value;
                var json = WorldJsonEdits.RewriteJson(originalText, group.ToArray(), limits);
                replacement = Snbt.Write(new LogicalMinecraftLeaf(new NbtString(json)), grammar, limits);
            }
            else
            {
                if (group.Count() != 1) throw new EditRejectedException(EditFailure.Precondition, "Command slot scope overlaps");
                var jsonString = first.Slot.Role == "reference.selector.name" || !modern && (first.Slot.Role == ClickCommandText.Role
                    || first.Slot.Role.StartsWith(source.Address.SemanticRole + ".text", StringComparison.Ordinal));
                var value = first.Reply.Text;
                if (first.Slot.Role == ClickCommandText.Role)
                {
                    var token = bytes.AsSpan(checked((int)span.StartByte), checked((int)span.LengthBytes));
                    var previous = jsonString ? WorldJsonEdits.ReadString(token)
                        : Snbt.ParseDocument(token.ToArray(), grammar, limits).Root.Value is LogicalMinecraftLeaf { Value: NbtString rawClick }
                            ? rawClick.Value : throw new EditRejectedException(EditFailure.Precondition, "Command click payload is not a string");
                    value = ClickCommandText.Rewrite(previous, first.Slot.SourceText, value, modern ? grammar : null, limits);
                }
                if (MinecraftProfiles.IsVersionedClassic(profile) && first.Slot.Role == source.Address.SemanticRole + ".message")
                {
                    static IEnumerable<string> Selectors(string text) => text.Split(' ').Where(token => token.StartsWith('@'));
                    if (!Selectors(first.Slot.SourceText).SequenceEqual(Selectors(value)))
                        throw new EditRejectedException(EditFailure.Precondition, "A translated message cannot change selector arguments");
                    replacement = encoding.GetBytes(value);
                }
                else replacement = jsonString
                    ? NativeJsonEdits.ReplaceStrings("\"\""u8.ToArray(), [(TypedPath.Root, "", value)], limits)
                    : Snbt.Write(new LogicalMinecraftLeaf(new NbtString(value)), grammar, limits);
            }
            if (replacement.Contains((byte)'\r') || replacement.Contains((byte)'\n')) throw new NotSupportedException("This command profile cannot encode a literal line break inside a syntax token");
            replacementBytes = checked(replacementBytes + replacement.Length);
            if (replacementBytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Command replacements exceed decode budget");
            edits.Add((span, replacement));
        }
        foreach (var group in conversions.GroupBy(item => SpanKey(item.Address)))
        {
            var replacement = WorldCommandKeyEdits.ComponentBytes(bytes, group.ToArray(), translations.Where(item => SpanKey(item.Slot.Address).Equals(group.Key)).ToArray(), profile, limits);
            if (replacement.Contains((byte)'\r') || replacement.Contains((byte)'\n')) throw new NotSupportedException("Command component cannot contain physical line breaks");
            replacementBytes = checked(replacementBytes + replacement.Length);
            if (replacementBytes > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Command replacements exceed decode budget");
            edits.Add((group.First().Address.Syntax!, replacement));
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
        using (WorkDiagnostics.Start("command.projection", phase: "projection"))
            Verify(source, new(address, script), translations, profile, limits, conversions, storageDisplayLists);
        return output;
    }
    private static (long Start, long Length, TypedPath? Path) SpanKey(SourceAddress address)
        => (address.Syntax!.StartByte, address.Syntax.LengthBytes, address.Syntax.NodePath);
    private static void Verify(CommandCandidate before, CommandCandidate after, IReadOnlyList<LiteralTranslation> translations, CapabilityProfile profile, DecodeLimits limits, IReadOnlyList<TranslationKeyConversion> conversions,
        IReadOnlySet<(string Storage, string Path)>? storageDisplayLists)
    {
        var original = CommandTextExtractor.Extract(before, profile, limits, storageDisplayLists: storageDisplayLists);
        var actual = CommandTextExtractor.Extract(after, profile, limits, storageDisplayLists: storageDisplayLists);
        var expected = original.Text.Occurrences.ToDictionary(item => SourceIdentity.Occurrence(item.Address).Digest, item => item.Text);
        foreach (var item in translations.Where(item => item.Slot.Role != "reference.selector.name")) expected[SourceIdentity.Occurrence(item.Slot.Address).Digest] = item.Reply.Text;
        static string Id(SourceAddress address) => SourceIdentity.Occurrence(address).Digest.Hex;
        var keys = original.Text.TranslationKeys.ToDictionary(item => Id(item.Address), item => item.Key, StringComparer.Ordinal);
        var referenceChanges = translations.Where(item => item.Slot.Role == "reference.selector.name").ToDictionary(item => item.Slot.Address, item => item.Reply.Text);
        foreach (var conversion in conversions)
        {
            if (!expected.Remove(SourceIdentity.Occurrence(conversion.Slot.Address).Digest)) throw new EditRejectedException(EditFailure.Precondition, "Converted command slot is missing");
            keys.Add(Id(WorldCommandKeyEdits.Child(conversion, "translation.key", PathSegment.ForKey("translate"))), conversion.Dependency.Key);
            if (conversion.Reply.Text.Contains('%')) expected.Add(SourceIdentity.Occurrence(WorldCommandKeyEdits.Child(conversion, conversion.Slot.Role, PathSegment.ForKey("with"), PathSegment.ForIndex(0))).Digest, "%");
        }
        var result = actual.Text.Occurrences.ToDictionary(item => SourceIdentity.Occurrence(item.Address).Digest, item => item.Text);
        if (result.Count != expected.Count || expected.Any(item => !result.TryGetValue(item.Key, out var value) || value != item.Value))
            throw new EditRejectedException(EditFailure.Precondition, "Command candidate text projection differs");
        if (!keys.Select(item => (item.Key, item.Value)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.Text.TranslationKeys.Select(item => (Id(item.Address), item.Key)).OrderBy(item => item.Item1, StringComparer.Ordinal))
            || !original.References.Select(item => (Id(item.Address), referenceChanges.GetValueOrDefault(item.Address, item.Name), item.Negated)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.References.Select(item => (Id(item.Address), item.Name, item.Negated)).OrderBy(item => item.Item1, StringComparer.Ordinal))
            || !original.Text.Diagnostics.Select(item => (Id(item.Address), item.Code)).OrderBy(item => item.Item1, StringComparer.Ordinal).SequenceEqual(actual.Text.Diagnostics.Select(item => (Id(item.Address), item.Code)).OrderBy(item => item.Item1, StringComparer.Ordinal)))
            throw new EditRejectedException(EditFailure.Precondition, "Command candidate protected references or diagnostics differ");
    }
}
