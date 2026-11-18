using System.Collections.Immutable;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

public sealed record ComponentSelectorReference(SourceAddress Address, string Selector, SelectorNameResult Parsed);
public sealed record ComponentReferenceResult(ImmutableArray<ComponentSelectorReference> Selectors, ImmutableArray<ExtractionDiagnostic> Diagnostics)
{
    public ReferenceCompleteness Completeness => Diagnostics.IsEmpty && Selectors.All(item => item.Parsed.Complete)
        ? ReferenceCompleteness.Complete : ReferenceCompleteness.Unresolved;
}

public static class NativeComponentReferences
{
    public static ComponentReferenceResult Json(SourceAddress source, byte[] component, CapabilityProfile profile, DecodeLimits? limits = null)
    {
        Check(source);
        if (source.Artifact.Kind != ArtifactKind.Json && !(source.Artifact.Kind == ArtifactKind.Archive && (source.Artifact.ContainerEntry?.EndsWith(".json", StringComparison.Ordinal) == true || source.Artifact.ContainerEntry == "pack.mcmeta")))
            throw new ArgumentException("Native JSON components require a JSON artifact or exact JSON archive entry", nameof(source));
        return ReadJson(source, component, profile, limits ?? DecodeLimits.Default, null);
    }
    public static ComponentReferenceResult EmbeddedJson(SourceAddress source, string text, CapabilityProfile profile, DecodeLimits? limits = null)
    {
        Check(source); limits ??= DecodeLimits.Default;
        return ReadJson(source, NativeJsonStrings.EncodeJavaDocument(text, limits), profile, limits, text);
    }
    private static ComponentReferenceResult ReadJson(SourceAddress source, byte[] bytes, CapabilityProfile profile, DecodeLimits limits, string? embeddedText)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var document = BoundedJson.ReadDecoded(bytes, limits);
        var reader = new Reader(limits);
        void Visit(SourceAddress root)
        {
            var local = root.ComponentPath ?? new TypedPath(root.NodePath.Segments.Skip(source.NodePath.Segments.Length));
            var value = JsonAt(document.RootElement, local);
            if (value.ValueKind != JsonValueKind.Object) return;
            var modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
            if (value.TryGetNativeProperty("selector", out var selector) && selector.ValueKind == JsonValueKind.String)
                reader.Selector(Child(root, "selector"), NativeJsonStrings.Read(selector));
            if (value.TryGetNativeProperty("nbt", out _) || value.TryGetNativeProperty("score", out _) || value.TryGetNativeProperty(modern ? "click_event" : "clickEvent", out _)) reader.Dynamic(root);
            if (value.TryGetNativeProperty(modern ? "hover_event" : "hoverEvent", out var hover)
                && (hover.ValueKind != JsonValueKind.Object || !hover.TryGetNativeProperty("action", out var action) || action.ValueKind != JsonValueKind.String || NativeJsonStrings.Read(action) != "show_text")) reader.Dynamic(root);
        }
        var result = embeddedText is null ? TextComponentExtractor.Json(source, bytes, profile, limits, componentRoot: Visit)
            : TextComponentExtractor.EmbeddedJsonString(source, embeddedText, profile, limits, componentRoot: Visit);
        return reader.Finish(result);
    }
    public static ComponentReferenceResult Nbt(SourceAddress source, NbtValue component, CapabilityProfile profile, DecodeLimits? limits = null)
    {
        Check(source); limits ??= DecodeLimits.Default;
        var reader = new Reader(limits);
        var result = TextComponentExtractor.Nbt(source, component, profile, limits, componentRoot: root =>
        {
            var local = new TypedPath(root.NodePath.Segments.Skip(source.NodePath.Segments.Length));
            if (NbtLocator.Resolve(component, local).Value is not NbtCompound value) return;
            var fields = value.Entries.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
            if (fields.GetValueOrDefault("selector") is NbtString selector) reader.Selector(Child(root, "selector"), selector.Value);
            if (fields.ContainsKey("nbt") || fields.ContainsKey("score") || fields.ContainsKey("click_event")) reader.Dynamic(root);
            if (fields.TryGetValue("hover_event", out var hover))
            {
                var actions = hover is NbtCompound compound ? compound.Entries.Where(item => item.Name == "action").Take(2).ToArray() : [];
                if (actions.Length != 1 || actions[0].Value is not NbtString { Value: "show_text" }) reader.Dynamic(root);
            }
        });
        return reader.Finish(result);
    }
    private static void Check(SourceAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Syntax is not null) throw new ArgumentException("Native components require an original tree address, not command syntax", nameof(source));
    }
    private static SourceAddress Child(SourceAddress root, string field)
        => new(root.Artifact, root.ComponentPath is null ? At(root.NodePath, field) : root.NodePath, "reference.component.selector", root.Dimension, root.Chunk,
            root.ComponentPath is null ? null : At(root.ComponentPath, field));
    private static JsonElement JsonAt(JsonElement root, TypedPath path)
    {
        foreach (var segment in path.Segments)
        {
            if (segment.Kind == PathSegmentKind.Index) root = root[segment.Index!.Value];
            else if (root.TryGetNativeProperty(segment.Key!, out var value)) root = value;
            else throw new InvalidDataException("Declared component root is missing from its native JSON");
        }
        return root;
    }
    private sealed class Reader(DecodeLimits limits)
    {
        private readonly List<ComponentSelectorReference> selectors = [];
        private readonly List<ExtractionDiagnostic> diagnostics = [];
        private long retained;
        private void Charge(SourceAddress address, long characters)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, characters));
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Native references exceed retention budget");
        }
        internal void Selector(SourceAddress address, string text)
        {
            var parsed = SelectorNames.Parse(text, limits);
            Charge(address, text.Length);
            foreach (var name in parsed.Names) Charge(address, name.Name.Length);
            foreach (var diagnostic in parsed.Diagnostics) Charge(address, diagnostic.Code.Length);
            selectors.Add(new(address, text, parsed));
        }
        internal void Dynamic(SourceAddress address)
        {
            const string code = "DynamicComponentReference"; Charge(address, code.Length); diagnostics.Add(new(address, code));
        }
        internal ComponentReferenceResult Finish(TextExtractionResult text)
        {
            if (checked(retained + TextExtractionBudget.ResultBytes(text)) > limits.MaxDecodedBytes)
                throw new CodecException(CodecError.LimitExceeded, 0, "Combined native extraction exceeds retention budget");
            diagnostics.AddRange(text.Diagnostics);
            return new(selectors.ToImmutableArray(), diagnostics.ToImmutableArray());
        }
    }
}
