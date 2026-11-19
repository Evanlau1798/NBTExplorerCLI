using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal static class WorldCommandKeyEdits
{
    internal static byte[] ComponentBytes(byte[] source, IReadOnlyList<TranslationKeyConversion> conversions, IReadOnlyList<LiteralTranslation> translations, CapabilityProfile profile, DecodeLimits limits)
    {
        var conversion = conversions[0];
        var syntax = conversion.Address.Syntax ?? throw new EditRejectedException(EditFailure.Precondition, "Command key requires an exact syntax root");
        if (conversion.Expected is not NbtString || syntax.StartByte > source.Length || syntax.LengthBytes > source.Length - syntax.StartByte)
            throw new EditRejectedException(EditFailure.Precondition, "Command key span exceeds its source");
        var bytes = source.AsSpan(checked((int)syntax.StartByte), checked((int)syntax.LengthBytes)).ToArray(); var encoding = new UTF8Encoding(false, true);
        var raw = encoding.GetString(bytes);
        if (conversions.Any(item => item.Expected is not NbtString value || value.Value != raw)) throw new EditRejectedException(EditFailure.Precondition, "Command component differs from its declared raw root");
        var modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
        var grammar = MinecraftProfiles.SnbtGrammar(profile);
        if (conversion.Address.ComponentPath is not null)
        {
            if (conversions.Any(item => item.Address.ComponentPath is null) || translations.Any(item => item.Slot.Address.ComponentPath is null)) throw new EditRejectedException(EditFailure.Precondition, "Command encoded component layers differ");
            var parsed = Snbt.ParseDocument(bytes, grammar, limits);
            if (parsed.Root.Value is not LogicalMinecraftLeaf { Value: NbtString json }) throw new EditRejectedException(EditFailure.Precondition, "Command encoded JSON root is not a string");
            var keyed = NativeJsonKeyEdits.Convert(NativeJsonStrings.EncodeJavaDocument(json.Value, limits), conversions.Select(item => (item.Address.ComponentPath!, item.Slot.SourceText, item.Dependency.Key, item.Reply.Text.Contains('%'))), limits);
            var output = NativeJsonEdits.ReplaceStrings(keyed, WorldJsonEdits.LiteralChanges(keyed, translations, item => item.Slot.Address.ComponentPath!, limits), limits);
            return Snbt.Write(new LogicalMinecraftLeaf(new NbtString(encoding.GetString(output))), grammar, limits);
        }
        if (conversions.Count != 1 || translations.Count != 0) throw new EditRejectedException(EditFailure.Precondition, "Direct command component roots overlap");
        if (!modern && conversion.Slot.Role.StartsWith("command.", StringComparison.Ordinal))
            return NativeJsonKeyEdits.Convert(bytes, [(TypedPath.Root, conversion.Slot.SourceText, conversion.Dependency.Key, conversion.Reply.Text.Contains('%'))], limits);
        var component = Snbt.ParseDocument(bytes, grammar, limits);
        var document = MinecraftNbtAdapter.Encode(new("", component.Root.Value), profile, limits);
        var converted = new NbtDocument("", WorldKeyEdits.Component(document.Root, conversion));
        return Snbt.Write(MinecraftNbtAdapter.Decode(converted, profile, limits).Root, grammar, limits);
    }
    internal static SourceAddress Child(TranslationKeyConversion conversion, string role, params PathSegment[] segments)
    {
        var root = conversion.Address; var syntax = root.Syntax!;
        var component = root.ComponentPath is null ? null : new TypedPath(root.ComponentPath.Segments.Concat(segments));
        var locator = component is not null ? syntax : new SyntaxLocator(syntax.GrammarProfile, syntax.GrammarVersion, syntax.StartByte, syntax.LengthBytes,
            syntax.PreimageHash, syntax.Encoding, syntax.Newline, new(syntax.NodePath!.Segments.Concat(segments)));
        return new(root.Artifact, root.NodePath, role, root.Dimension, root.Chunk, component, locator);
    }
}
