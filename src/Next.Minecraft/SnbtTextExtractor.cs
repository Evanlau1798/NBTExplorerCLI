using System.Text;
using System.Collections.Immutable;
using Next.Domain;
using Next.Formats;

namespace Next.Minecraft;

internal static class SnbtTextExtractor
{
    internal static TextExtractionResult ItemComponent(SourceAddress source, string syntax, CapabilityProfile profile, string name, DecodeLimits limits, SnapshotScope scope)
        => Read(source, syntax, profile, limits, scope, (reader, root) => reader.ItemComponent(name, root, TypedPath.Root), false);
    internal static TextExtractionResult Component(SourceAddress source, string syntax, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope)
        => Read(source, syntax, profile, limits, scope, (reader, root) => reader.Component(root, TypedPath.Root, source.SemanticRole), false);
    internal static TextExtractionResult LegacyItemTag(SourceAddress source, string syntax, CapabilityProfile profile, string? itemId, DecodeLimits limits, SnapshotScope scope, Action<SourceAddress>? componentRoot = null)
        => Read(source, syntax, profile, limits, scope, (reader, root) => reader.LegacyTag(root, itemId), componentRoot: componentRoot);
    internal static TextExtractionResult BlockEntity(SourceAddress source, string syntax, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope, string? knownIdentity = null, Action<SourceAddress>? componentRoot = null)
        => Read(source, syntax, profile, limits, scope, (reader, root) => reader.BlockEntity(root, TypedPath.Root, knownIdentity), componentRoot: componentRoot);
    internal static TextExtractionResult Entity(SourceAddress source, string syntax, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope, string? knownIdentity = null, Action<SourceAddress>? componentRoot = null)
        => Read(source, syntax, profile, limits, scope, (reader, root) => reader.Entity(root, TypedPath.Root, knownIdentity), componentRoot: componentRoot);
    private static TextExtractionResult Read(SourceAddress source, string syntax, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope, Action<NbtTextReader, NbtValue> extract, bool compound = true, Action<SourceAddress>? componentRoot = null)
    {
        if (source.Syntax is not null || source.ComponentPath is not null) throw new CodecException(CodecError.UnsupportedProfile, 0, "Nested syntax binding is unsupported");
        var encoding = new UTF8Encoding(false, true);
        if (encoding.GetByteCount(syntax) > limits.MaxDecodedBytes) throw new SnbtException(SnbtError.LimitExceeded, 0, "SNBT string exceeds decode budget");
        var grammar = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt" ? SnbtProfile.Java1215 : SnbtProfile.JavaLegacy;
        var parsed = Snbt.ParseDocument(encoding.GetBytes(syntax), grammar, limits);
        var document = MinecraftNbtAdapter.Encode(new("", parsed.Root.Value), profile, limits);
        if (compound && document.Root is not NbtCompound) throw new SnbtException(SnbtError.InvalidToken, 0, "Item tag requires an SNBT compound");
        var binding = new SourceAddress(source.Artifact, TypedPath.Root, source.SemanticRole, source.Dimension, source.Chunk);
        var output = new TextExtractionCollector(binding, false, limits, scope) { ComponentRoot = componentRoot is null ? null : address => componentRoot(Bind(address)) };
        extract(new(binding, profile, profile, output, limits), document.Root);
        var result = output.Finish();
        return new(result.Occurrences.Select(value => value with { Address = Bind(value.Address) }).ToImmutableArray(),
            result.TranslationKeys.Select(value => value with { Address = Bind(value.Address) }).ToImmutableArray(),
            result.Diagnostics.Select(value => value with { Address = Bind(value.Address) }).ToImmutableArray(),
            result.PendingCommands.Select(value => value with { Address = Bind(value.Address) }).ToImmutableArray(),
            result.Unclassified.Select(value => value with { Address = Bind(value.Address) }).ToImmutableArray());

        SourceAddress Bind(SourceAddress address)
        {
            var logical = new List<PathSegment>(); NbtValue physical = document.Root; var wrapper = false;
            foreach (var segment in address.NodePath.Segments)
            {
                var skip = wrapper && segment.Kind == PathSegmentKind.Key && segment.Key == "";
                physical = NbtLocator.Resolve(physical, new([segment])).Value ?? throw new SnbtException(SnbtError.Missing, 0, "Missing syntax node");
                if (skip) wrapper = false;
                else
                {
                    logical.Add(segment);
                    wrapper = profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && segment.Kind == PathSegmentKind.Index
                        && physical is NbtCompound { Entries.Length: 1 } compound && compound.Entries[0].Name == "";
                }
            }
            var span = parsed.Resolve(new(logical)).Span;
            var locator = new SyntaxLocator(grammar == SnbtProfile.JavaLegacy ? "java-snbt-legacy" : "java-snbt-1.21.5", 1,
                span.StartByte, span.LengthBytes, parsed.SourceHash.Hex, "utf8-java-string", SyntaxLocator.DetectNewline(syntax), new(logical));
            return new(source.Artifact, source.NodePath, address.SemanticRole, source.Dimension, source.Chunk, address.ComponentPath, locator, address.JsonTarget);
        }
    }
}
