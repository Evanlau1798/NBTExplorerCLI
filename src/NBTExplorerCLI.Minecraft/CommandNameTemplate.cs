using System.Collections.Immutable;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

public sealed record CommandNameTemplate(SourceAddress Root, string EntityType, string Name, ImmutableArray<TextOccurrence> Parts);

internal sealed partial class CommandTextReader
{
    private void ReadSummonNameTemplate(int start, int end, TypedPath grammarPath, string type, TextExtractionResult extraction)
    {
        var modern = profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
        var syntax = Snbt.ParseDocument(Utf8.GetBytes(source.Command[start..end]), modern ? SnbtProfile.Java1215 : SnbtProfile.JavaThrough1214, limits);
        if (syntax.Root.Value is not LogicalMinecraftCompound root) return;
        var encoded = root.Entries.SingleOrDefault(entry => entry.Name == "CustomName");
        if (encoded is null) return;
        var role = type == "minecraft:armor_stand" ? "armor_stand.name" : "entity.name";
        var projected = extraction.Occurrences.Where(item => item.Address.SemanticRole == role
            && item.Address.Syntax?.NodePath?.Segments.FirstOrDefault()?.Key == "CustomName").ToArray();
        var parts = new List<string>();
        var staticName = false;
        if (modern) staticName = Logical(encoded.Value, parts);
        else if (encoded.Value is LogicalMinecraftLeaf { Value: NbtString json })
        {
            using var document = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(json.Value, limits), limits);
            staticName = JsonName(document.RootElement, parts);
        }
        if (!staticName || parts.Count == 0 || !parts.SequenceEqual(projected.Select(item => item.Text)))
        { Unknown(grammarPath, start, end - start, "UnsupportedSummonNameTemplate"); return; }
        var name = string.Concat(parts);
        var span = syntax.Resolve(new TypedPath([PathSegment.ForKey("CustomName")])).Span;
        var address = Address(At(grammarPath, "CustomName"), start, 0, role, span.StartByte, span.LengthBytes);
        Charge(address, name);
        nameTemplate!(new(address, type, name,
            projected.Select(part => part with { Address = BindComponent(part.Address, grammarPath, start, true, null) }).ToImmutableArray()));
    }

    private static bool OtherNameContent(string key) => key is "translate" or "score" or "selector" or "keybind" or "nbt" or "object" or "atlas" or "player";

    private static bool Logical(LogicalMinecraftValue value, List<string> parts)
    {
        if (value is LogicalMinecraftLeaf { Value: NbtString text }) { parts.Add(text.Value); return true; }
        if (value is LogicalMinecraftList list) return list.Items.All(item => Logical(item, parts));
        if (value is not LogicalMinecraftCompound compound || compound.Entries.Any(entry => OtherNameContent(entry.Name))
            || compound.Entries.SingleOrDefault(entry => entry.Name == "text")?.Value is not LogicalMinecraftLeaf { Value: NbtString literal }) return false;
        parts.Add(literal.Value);
        return compound.Entries.SingleOrDefault(entry => entry.Name == "extra")?.Value is not { } extra || Logical(extra, parts);
    }

    private static bool JsonName(JsonElement value, List<string> parts)
    {
        if (value.ValueKind == JsonValueKind.String) { parts.Add(NativeJsonStrings.Read(value)); return true; }
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(item => JsonName(item, parts));
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(property => OtherNameContent(NativeJsonStrings.PropertyName(property)))
            || !value.TryGetNativeProperty("text", out var literal) || literal.ValueKind != JsonValueKind.String) return false;
        parts.Add(NativeJsonStrings.Read(literal));
        return !value.TryGetNativeProperty("extra", out var extra) || JsonName(extra, parts);
    }
}
