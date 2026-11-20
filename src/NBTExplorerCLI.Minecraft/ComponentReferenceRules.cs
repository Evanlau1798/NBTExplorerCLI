using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

internal static class ComponentReferenceRules
{
    internal static (string Id, string Path, bool Interpreted, bool OpaqueClick)? JsonStorage(JsonElement value, DecodeLimits limits)
    {
        var id = Field(value, "storage"); var path = Field(value, "nbt");
        if (id is null || path is null || !MinecraftIdentifier.ResourceLocation(id)) return null;
        var interpreted = value.TryGetNativeProperty("interpret", out var flag) && flag.ValueKind == JsonValueKind.True
            && !OtherComponentContent(field => value.TryGetNativeProperty(field, out _));
        var opaqueClick = value.TryGetNativeProperty("clickEvent", out var click) && Click(click, null, limits) is null;
        return (id, path, interpreted, opaqueClick);
    }

    internal static bool OtherComponentContent(Func<string, bool> hasField)
        => hasField("text") || hasField("translate") || hasField("selector") || hasField("score") || hasField("keybind")
            || hasField("entity") || hasField("block");

    private static bool Inert(string? action) => action is "open_url" or "change_page" or "copy_to_clipboard";

    internal static string CommandField(bool modern) => modern ? "command" : "value";
    internal static ClickCommandText.Binding? Click(JsonElement value, SnbtProfile? grammar, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, CommandField(grammar.HasValue)), grammar, limits);

    internal static ClickCommandText.Binding? Click(NbtValue value, SnbtProfile grammar, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, "command"), grammar, limits);

    internal static ClickCommandText.Binding? Click(LogicalMinecraftValue value, SnbtProfile? grammar, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, CommandField(grammar.HasValue)), grammar, limits);

    private static ClickCommandText.Binding? Click(string? action, string? command, SnbtProfile? grammar, DecodeLimits limits)
        => Inert(action) ? ClickCommandText.NoText : action == "run_command" && command is not null ? ClickCommandText.Read(command, grammar, limits) : null;

    private static string? Field(JsonElement value, string name)
        => value.TryGetNativeProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(property) : null;
    private static string? Field(NbtValue value, string name)
        => value is NbtCompound compound && compound.Entries.Where(entry => entry.Name == name).Take(2).ToArray() is [{ Value: NbtString text }] ? text.Value : null;
    private static string? Field(LogicalMinecraftValue value, string name)
        => value is LogicalMinecraftCompound compound
            && compound.Entries.Where(entry => entry.Name == name).Take(2).ToArray() is [{ Value: LogicalMinecraftLeaf { Value: NbtString text } }] ? text.Value : null;

    internal static bool HasFixedScoreHolder(JsonElement value) => FixedScore(Field(value, "name"), Field(value, "objective"));
    internal static bool HasFixedScoreHolder(NbtValue value) => FixedScore(Field(value, "name"), Field(value, "objective"));
    internal static bool HasFixedScoreHolder(LogicalMinecraftValue value) => FixedScore(Field(value, "name"), Field(value, "objective"));

    private static bool FixedScore(string? holder, string? objective) => objective is { Length: > 0 }
        && (holder == "@s" || holder is { Length: > 1 } && holder[0] is '#' or '$'
            && holder.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '#' or '$'));
}
