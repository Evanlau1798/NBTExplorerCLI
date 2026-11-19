using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

public static class TextComponentExtractor
{
    private static readonly string[] ContentKeys = ["text", "translate", "score", "selector", "keybind", "nbt"];
    public static TextExtractionResult Json(SourceAddress source, byte[] component, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null)
    {
        var modern = Check(source, profile);
        var embedded = source.Artifact.Kind is ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities
            || source.Artifact.Kind == ArtifactKind.Archive && source.Artifact.ContainerEntry?.EndsWith(".nbt", StringComparison.Ordinal) == true;
        if (embedded && modern) throw Unsupported();
        return ReadJson(source, component, profile, limits, scope, embedded, componentRoot);
    }
    public static TextExtractionResult EmbeddedJsonString(SourceAddress source, string component, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null)
    {
        if (Check(source, profile)) throw Unsupported();
        return ReadJson(source, NativeJsonStrings.EncodeJavaDocument(component, limits), profile, limits, scope, true, componentRoot);
    }
    internal static TextExtractionResult NativeJson(SourceAddress source, byte[] component, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope, Action<SourceAddress>? componentRoot = null)
    {
        _ = Check(source, profile); return ReadJson(source, component, profile, limits, scope, false, componentRoot);
    }
    private static TextExtractionResult ReadJson(SourceAddress source, byte[] component, CapabilityProfile profile, DecodeLimits? limits, SnapshotScope? scope, bool embedded, Action<SourceAddress>? componentRoot)
    {
        limits ??= DecodeLimits.Default;
        using var json = BoundedJson.ReadDecoded(component, limits);
        var collector = new TextExtractionCollector(source, embedded, limits, scope) { ComponentRoot = componentRoot };
        JsonValue(json.RootElement, TypedPath.Root, source.SemanticRole, profile, collector);
        return collector.Finish();
    }
    public static TextExtractionResult Nbt(SourceAddress source, NbtValue component, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null)
    {
        if (!Check(source, profile)) throw Unsupported();
        limits ??= DecodeLimits.Default; _ = BinaryNbt.MeasureDocument(new("", component), limits);
        var collector = new TextExtractionCollector(source, false, limits, scope) { ComponentRoot = componentRoot };
        WalkNbt(component, TypedPath.Root, source.SemanticRole, profile, collector);
        return collector.Finish();
    }
    private static bool Check(SourceAddress source, CapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(profile);
        if (source.ComponentPath is not null || source.JsonTarget != JsonTargetKind.Value) throw new ArgumentException("Requires an exact component root", nameof(source));
        if (!profile.Features.TryGetValue(ProfileFacet.TextSchema, out var text) || text.Operations.Read != SupportLevel.Supported
            || text.Schema is not ("java-text-json" or "java-text-nbt")) throw Unsupported();
        if (text.Schema == "java-text-json" && text.Version != 1 || text.Schema == "java-text-nbt" && text.Version is not (1 or 2)) throw Unsupported();
        return text.Schema == "java-text-nbt";
    }
    private static void JsonValue(JsonElement value, TypedPath path, string role, CapabilityProfile profile, TextExtractionCollector output, bool argument = false)
    {
        if (value.ValueKind == JsonValueKind.String) { output.RegisterComponent(output.Address(path, role)); output.Literal(path, NativeJsonStrings.Read(value), role); return; }
        if (argument && value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return;
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0; foreach (var child in value.EnumerateArray()) JsonValue(child, At(path, index++), role, profile, output, argument);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) { output.Diagnostic(path, "InvalidComponent"); return; }
        var fields = value.EnumerateObject().ToDictionary(NativeJsonStrings.PropertyName, property => property.Value, StringComparer.Ordinal);
        var schema = profile.Features[ProfileFacet.TextSchema];
        var modern = schema.Schema == "java-text-nbt";
        var objectContent = modern && schema.Version == 2 && (fields.ContainsKey("object") || fields.ContainsKey("atlas") || fields.ContainsKey("player"));
        var contents = ContentKeys.Count(fields.ContainsKey) + (objectContent ? 1 : 0);
        if (contents != 1) { output.Diagnostic(path, contents > 1 ? "AmbiguousComponent" : "UnknownComponent"); return; }
        output.RegisterComponent(output.Address(path, role));
        if (fields.TryGetValue("selector", out var selector) && selector.ValueKind != JsonValueKind.String) { output.Diagnostic(At(path, "selector"), "InvalidComponent"); return; }
        if (objectContent && fields.TryGetValue("object", out var objectKind) && (objectKind.ValueKind != JsonValueKind.String || NativeJsonStrings.Read(objectKind) is not ("atlas" or "player")))
        { output.Diagnostic(path, "UnknownComponent"); return; }
        if (fields.TryGetValue("text", out var text))
        {
            if (text.ValueKind == JsonValueKind.String) output.Literal(At(path, "text"), NativeJsonStrings.Read(text), role);
            else output.Diagnostic(At(path, "text"), "InvalidComponent");
        }
        if (fields.TryGetValue("translate", out var key))
        {
            if (key.ValueKind == JsonValueKind.String) output.Key(At(path, "translate"), NativeJsonStrings.Read(key));
            else output.Diagnostic(At(path, "translate"), "InvalidComponent");
            if (fields.TryGetValue("fallback", out var fallback))
            {
                if (fallback.ValueKind == JsonValueKind.String) output.Literal(At(path, "fallback"), NativeJsonStrings.Read(fallback), role + ".fallback");
                else output.Diagnostic(At(path, "fallback"), "InvalidComponent");
            }
            if (fields.TryGetValue("with", out var arguments)) JsonList(arguments, At(path, "with"), role, profile, output, argument: true);
        }
        if (fields.TryGetValue("extra", out var extra)) JsonList(extra, At(path, "extra"), role, profile, output);
        if ((fields.ContainsKey("selector") || fields.ContainsKey("nbt")) && fields.TryGetValue("separator", out var separator)) JsonValue(separator, At(path, "separator"), role, profile, output);
        var clickKey = modern ? "click_event" : "clickEvent";
        if (fields.TryGetValue(clickKey, out var click)
            && ComponentReferenceRules.Click(click, modern ? MinecraftProfiles.SnbtGrammar(profile) : null, output.Limits)?.Text is { } message)
            output.Literal(At(At(path, clickKey), ComponentReferenceRules.CommandField(modern)), message, ClickCommandText.Role);
        var hoverKey = modern ? "hover_event" : "hoverEvent";
        if (fields.TryGetValue(hoverKey, out var hover) && hover.ValueKind == JsonValueKind.Object)
        {
            var eventFields = hover.EnumerateObject().ToDictionary(NativeJsonStrings.PropertyName, property => property.Value, StringComparer.Ordinal);
            if (eventFields.TryGetValue("action", out var action) && action.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(action) == "show_text")
            {
                var member = modern ? MinecraftProfiles.NativeHoverField(profile) : "contents";
                if (!modern && !eventFields.ContainsKey(member) && eventFields.ContainsKey("value")) member = "value";
                if (eventFields.TryGetValue(member, out var content)) JsonValue(content, At(At(path, hoverKey), member), role + ".hover", profile, output);
                else output.Diagnostic(At(path, hoverKey), "InvalidComponent");
            }
        }
    }
    private static void JsonList(JsonElement value, TypedPath path, string role, CapabilityProfile profile, TextExtractionCollector output, bool argument = false)
    {
        if (value.ValueKind != JsonValueKind.Array) { output.Diagnostic(path, "InvalidComponent"); return; }
        JsonValue(value, path, role, profile, output, argument);
    }
    private static void WalkNbt(NbtValue value, TypedPath path, string role, CapabilityProfile profile, TextExtractionCollector output, bool argument = false)
    {
        if (value is NbtString textValue) { output.RegisterComponent(output.Address(path, role)); output.Literal(path, textValue.Value, role); return; }
        if (argument && value is NbtByte or NbtShort or NbtInt or NbtLong or NbtFloat or NbtDouble) return;
        if (value is NbtList list)
        {
            for (var index = 0; index < list.Items.Length; index++)
            {
                var child = list.Items[index]; var childPath = At(path, index);
                if (profile.Lists.Encoding == LogicalListEncoding.CompoundWrapper && profile.Lists.WrapperKey == "" && child is NbtCompound { Entries.Length: 1 } wrapper && wrapper.Entries[0].Name == "")
                { child = wrapper.Entries[0].Value; childPath = At(childPath, ""); }
                WalkNbt(child, childPath, role, profile, output, argument);
            }
            return;
        }
        if (value is not NbtCompound compound) { output.Diagnostic(path, "InvalidComponent"); return; }
        var fields = new Dictionary<string, NbtValue>(StringComparer.Ordinal);
        foreach (var entry in compound.Entries)
            if (!fields.TryAdd(entry.Name, entry.Value)) { output.Diagnostic(path, "AmbiguousComponent"); return; }
        var objectContent = profile.Features[ProfileFacet.TextSchema].Version == 2 && (fields.ContainsKey("object") || fields.ContainsKey("atlas") || fields.ContainsKey("player"));
        var contents = ContentKeys.Count(fields.ContainsKey) + (objectContent ? 1 : 0);
        if (contents != 1) { output.Diagnostic(path, contents > 1 ? "AmbiguousComponent" : "UnknownComponent"); return; }
        output.RegisterComponent(output.Address(path, role));
        if (fields.TryGetValue("selector", out var selector) && selector is not NbtString) { output.Diagnostic(At(path, "selector"), "InvalidComponent"); return; }
        if (objectContent && fields.TryGetValue("object", out var objectKind) && objectKind is not NbtString { Value: "atlas" or "player" })
        { output.Diagnostic(path, "UnknownComponent"); return; }
        if (fields.TryGetValue("text", out var text))
        {
            if (text is NbtString literal) output.Literal(At(path, "text"), literal.Value, role);
            else output.Diagnostic(At(path, "text"), "InvalidComponent");
        }
        if (fields.TryGetValue("translate", out var key))
        {
            if (key is NbtString reference) output.Key(At(path, "translate"), reference.Value);
            else output.Diagnostic(At(path, "translate"), "InvalidComponent");
            if (fields.TryGetValue("fallback", out var fallback))
            {
                if (fallback is NbtString literal) output.Literal(At(path, "fallback"), literal.Value, role + ".fallback");
                else output.Diagnostic(At(path, "fallback"), "InvalidComponent");
            }
            if (fields.TryGetValue("with", out var arguments)) WalkNbtList(arguments, At(path, "with"), role, profile, output, argument: true);
        }
        if (fields.TryGetValue("extra", out var extra)) WalkNbtList(extra, At(path, "extra"), role, profile, output);
        if ((fields.ContainsKey("selector") || fields.ContainsKey("nbt")) && fields.TryGetValue("separator", out var separator)) WalkNbt(separator, At(path, "separator"), role, profile, output);
        if (fields.TryGetValue("click_event", out var click) && ComponentReferenceRules.Click(click, MinecraftProfiles.SnbtGrammar(profile), output.Limits)?.Text is { } message)
            output.Literal(At(At(path, "click_event"), "command"), message, ClickCommandText.Role);
        if (fields.TryGetValue("hover_event", out var hover) && hover is NbtCompound hoverFields)
        {
            var eventFields = new Dictionary<string, NbtValue>(StringComparer.Ordinal);
            foreach (var entry in hoverFields.Entries)
                if (!eventFields.TryAdd(entry.Name, entry.Value)) { output.Diagnostic(At(path, "hover_event"), "AmbiguousComponent"); return; }
            if (eventFields.GetValueOrDefault("action") is NbtString { Value: "show_text" })
            {
                var member = MinecraftProfiles.NativeHoverField(profile);
                if (eventFields.TryGetValue(member, out var content)) WalkNbt(content, At(At(path, "hover_event"), member), role + ".hover", profile, output);
                else output.Diagnostic(At(path, "hover_event"), "InvalidComponent");
            }
        }
    }
    private static void WalkNbtList(NbtValue value, TypedPath path, string role, CapabilityProfile profile, TextExtractionCollector output, bool argument = false)
    {
        if (value is not NbtList) { output.Diagnostic(path, "InvalidComponent"); return; }
        WalkNbt(value, path, role, profile, output, argument);
    }
    private static CodecException Unsupported() => new(CodecError.UnsupportedProfile, 0, "Unsupported text representation/profile");
}
