using Next.Domain;
using System.Text.Json;

namespace Next.Formats;

public static class TypedEditJson
{
    public const string Family = "nbtcli.next.typed-edit";
    public const int Version = 1;
    private sealed record EditDto(TypedEditAction? Action, TypedPath? Path, NbtValue? Expected, NbtValue? Replacement, TypedPath? Child, string? NewName);
    public static string Write(TypedEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ContractJson.Write(Family, new EditDto(edit.Action, edit.Path, edit.Expected, edit.Replacement,
            edit.Child is null ? null : new([edit.Child]), edit.NewName));
    }
    public static TypedEdit Read(string json) => ContractJson.Read(json, Family, data =>
    {
        ContractJson.Fields(data, "action", "path", "expected", "replacement", "child", "newName");
        var dto = ContractJson.Decode<EditDto>(data);
        var path = dto.Path ?? throw new JsonException("Missing edit path");
        var expected = dto.Expected ?? throw new JsonException("Missing expected typed value");
        return dto switch
        {
            { Action: TypedEditAction.Replace, Replacement: not null, Child: null, NewName: null } => TypedEdit.Replace(path, expected, dto.Replacement),
            { Action: TypedEditAction.Add, Replacement: not null, Child: not null, NewName: null } when dto.Child.Segments.Length == 1
                => TypedEdit.Add(path, expected, dto.Child.Segments[0], dto.Replacement),
            { Action: TypedEditAction.Remove, Replacement: null, Child: null, NewName: null } => TypedEdit.Remove(path, expected),
            { Action: TypedEditAction.Rename, Replacement: null, Child: null, NewName: not null } => TypedEdit.Rename(path, expected, dto.NewName),
            _ => throw new JsonException("Edit contains missing or mutually exclusive fields")
        };
    });
}
