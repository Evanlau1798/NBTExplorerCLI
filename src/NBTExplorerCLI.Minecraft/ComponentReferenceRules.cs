using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

internal static class ComponentReferenceRules
{
    private static bool Inert(string? action) => action is "open_url" or "change_page" or "copy_to_clipboard";

    internal static string CommandField(bool modern) => modern ? "command" : "value";
    internal static ClickCommandText.Binding? Click(JsonElement value, bool modern, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, CommandField(modern)), modern, limits);

    internal static ClickCommandText.Binding? Click(NbtValue value, bool modern, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, CommandField(modern)), modern, limits);

    internal static ClickCommandText.Binding? Click(LogicalMinecraftValue value, bool modern, DecodeLimits limits)
        => Click(Field(value, "action"), Field(value, CommandField(modern)), modern, limits);

    private static ClickCommandText.Binding? Click(string? action, string? command, bool modern, DecodeLimits limits)
        => Inert(action) ? ClickCommandText.NoText : action == "run_command" && command is not null ? ClickCommandText.Read(command, modern, limits) : null;

    private static string? Field(JsonElement value, string name)
        => value.TryGetNativeProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(property) : null;
    private static string? Field(NbtValue value, string name)
        => value is NbtCompound compound && compound.Entries.Where(entry => entry.Name == name).Take(2).ToArray() is [{ Value: NbtString text }] ? text.Value : null;
    private static string? Field(LogicalMinecraftValue value, string name)
        => value is LogicalMinecraftCompound compound
            && compound.Entries.Where(entry => entry.Name == name).Take(2).ToArray() is [{ Value: LogicalMinecraftLeaf { Value: NbtString text } }] ? text.Value : null;

    internal static bool IsSelfScore(JsonElement value) => value.TryGetNativeProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(name) == "@s"
        && value.TryGetNativeProperty("objective", out var objective) && objective.ValueKind == JsonValueKind.String && NativeJsonStrings.Read(objective).Length != 0;

    internal static bool IsSelfScore(NbtValue value) => value is NbtCompound compound
        && compound.Entries.Where(entry => entry.Name == "name").Take(2).ToArray() is [{ Value: NbtString { Value: "@s" } }]
        && compound.Entries.Where(entry => entry.Name == "objective").Take(2).ToArray() is [{ Value: NbtString { Value.Length: > 0 } }];

    internal static bool IsSelfScore(LogicalMinecraftValue value) => value is LogicalMinecraftCompound compound
        && compound.Entries.Where(entry => entry.Name == "name").Take(2).ToArray() is [{ Value: LogicalMinecraftLeaf { Value: NbtString { Value: "@s" } } }]
        && compound.Entries.Where(entry => entry.Name == "objective").Take(2).ToArray() is [{ Value: LogicalMinecraftLeaf { Value: NbtString { Value.Length: > 0 } } }];
}
