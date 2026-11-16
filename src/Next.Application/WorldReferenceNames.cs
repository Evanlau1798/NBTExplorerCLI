using Next.Domain;
using Next.Formats;
using System.Collections.Immutable;
using System.Text.Json;

namespace Next.Application;

internal sealed record WorldNamedTarget(SourceAddress Root, string Type, string Name, ImmutableArray<TextOccurrence> Parts, FormatFeature Grammar);
internal static class WorldReferenceNames
{
    internal static WorldNamedTarget Read(SourceAddress root, string type, NbtValue value, CapabilityProfile profile, DecodeLimits limits)
    {
        var parts = new List<TextOccurrence>(); var grammar = profile.Features[ProfileFacet.TextSchema]; long retained = 0;
        void Add(TypedPath path, string text, bool embedded)
        {
            var address = new SourceAddress(root.Artifact, embedded ? root.NodePath : new(root.NodePath.Segments.Concat(path.Segments)), root.SemanticRole,
                root.Dimension, root.Chunk, embedded ? path : null);
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, text.Length));
            if (retained > limits.MaxDecodedBytes || parts.Count >= 100_000) throw new CodecException(CodecError.LimitExceeded, 0, "Static name parts exceed retention budget");
            parts.Add(new(address, text, root.SemanticRole, true));
        }
        static TypedPath At(TypedPath path, string field) => new(path.Segments.Add(PathSegment.ForKey(field)));
        static bool Other(string field) => field is "translate" or "score" or "selector" or "keybind" or "nbt" or "object" or "atlas" or "player";
        void Json(JsonElement node, TypedPath path)
        {
            if (node.ValueKind == JsonValueKind.String) { Add(path, NativeJsonStrings.Read(node), true); return; }
            if (node.ValueKind == JsonValueKind.Array)
            { var index = 0; foreach (var child in node.EnumerateArray()) Json(child, new(path.Segments.Add(PathSegment.ForIndex(index++)))); return; }
            if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Any(item => Other(NativeJsonStrings.PropertyName(item)))
                || !node.TryGetNativeProperty("text", out var text) || text.ValueKind != JsonValueKind.String) Unknown();
            else Add(At(path, "text"), NativeJsonStrings.Read(text), true);
            if (node.TryGetNativeProperty("extra", out var extra)) Json(extra, At(path, "extra"));
        }
        void Nbt(NbtValue node, TypedPath path)
        {
            if (node is NbtString text) { Add(path, text.Value, false); return; }
            if (node is NbtList list)
            {
                for (var index = 0; index < list.Items.Length; index++)
                {
                    var child = list.Items[index]; var childPath = new TypedPath(path.Segments.Add(PathSegment.ForIndex(index)));
                    if (profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && child is NbtCompound { Entries.Length: 1 } wrapper && wrapper.Entries[0].Name == "")
                    { child = wrapper.Entries[0].Value; childPath = At(childPath, ""); }
                    Nbt(child, childPath);
                }
                return;
            }
            if (node is not NbtCompound compound) { Unknown(); return; }
            var fields = compound.Entries.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
            if (fields.Keys.Any(Other) || fields.GetValueOrDefault("text") is not NbtString literal) Unknown();
            else Add(At(path, "text"), literal.Value, false);
            if (fields.TryGetValue("extra", out var extra)) Nbt(extra, At(path, "extra"));
        }
        if (grammar.Schema == "java-text-json" && value is NbtString encoded)
        {
            using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(encoded.Value, limits), limits);
            Json(document.RootElement, TypedPath.Root);
        }
        else if (grammar.Schema == "java-text-nbt") Nbt(value, TypedPath.Root);
        else Unknown();
        if (parts.Count == 0) Unknown();
        return new(root, type, string.Concat(parts.Select(part => part.Text)), parts.ToImmutableArray(), grammar);
    }
    internal static void Unknown() => throw new EditRejectedException(EditFailure.Precondition, "An identifying component has no proven static plain name");
}
