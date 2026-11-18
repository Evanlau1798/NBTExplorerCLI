namespace NBTExplorerCLI.Domain;

public enum PatchKind { TypedEdit, LiteralTranslation, TranslationKeyConversion, ReferenceSynchronization, ArtifactRestore, ResourceGeneration }
public enum TypedEditAction { Replace, Add, Remove, Rename }

public sealed record TypedEdit
{
    public TypedEditAction Action { get; }
    public TypedPath Path { get; }
    public NbtValue Expected { get; }
    public NbtType ExpectedType => Expected.Type;
    public NbtValue? Replacement { get; }
    public PathSegment? Child { get; }
    public string? NewName { get; }
    private TypedEdit(TypedEditAction action, TypedPath path, NbtValue expected, NbtValue? replacement = null,
        PathSegment? child = null, string? newName = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expected);
        if (path.Segments.Length == 0 && action is TypedEditAction.Remove or TypedEditAction.Rename)
            throw new ArgumentException("The document root cannot be removed or renamed", nameof(path));
        (Action, Path, Expected, Replacement, Child, NewName) = (action, path, expected, replacement, child, newName);
    }
    public static TypedEdit Replace(TypedPath path, NbtValue expected, NbtValue replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return new(TypedEditAction.Replace, path, expected, replacement);
    }
    public static TypedEdit Add(TypedPath parent, NbtValue expectedParent, PathSegment child, NbtValue value)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(value);
        return new(TypedEditAction.Add, parent, expectedParent, value, child);
    }
    public static TypedEdit Remove(TypedPath path, NbtValue expected) => new(TypedEditAction.Remove, path, expected);
    public static TypedEdit Rename(TypedPath path, NbtValue expected, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(TypedEditAction.Rename, path, expected, newName: name);
    }
}

public enum EditFailure { Missing, Ambiguous, InvalidPath, Precondition, Overlap, InvalidStructure, ProtectedStructure }
public sealed class EditRejectedException(EditFailure failure, string message) : Exception(message)
{
    public EditFailure Failure { get; } = failure;
}
