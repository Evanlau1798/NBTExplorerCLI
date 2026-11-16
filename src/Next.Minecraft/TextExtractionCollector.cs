using System.Collections.Immutable;
using Next.Domain;

namespace Next.Minecraft;

internal sealed class TextExtractionCollector(SourceAddress source, bool embeddedJson, DecodeLimits limits, SnapshotScope? scope = null)
{
    internal SnapshotScope Scope { get; } = scope ?? SnapshotScope.All;
    internal DecodeLimits Limits { get; } = limits;
    internal Action<SourceAddress>? ComponentRoot { get; init; }
    internal bool Includes(string role) => WorldSourceScope.IncludesRole(role, Scope);
    internal bool CouldInclude(string role) => WorldSourceScope.CouldIncludeRole(role, Scope);
    private readonly List<TextOccurrence> occurrences = [];
    private readonly List<TranslationKeyReference> keys = [];
    private readonly List<ExtractionDiagnostic> diagnostics = [];
    private readonly List<CommandCandidate> commands = [];
    private readonly List<UnclassifiedTextCandidate> unclassified = [];
    private long retainedBytes;
    internal SourceAddress Address(TypedPath path, string role, JsonTargetKind target = JsonTargetKind.Value)
    {
        var node = embeddedJson ? source.NodePath : new TypedPath(source.NodePath.Segments.Concat(path.Segments));
        return new(source.Artifact, node, role, source.Dimension, source.Chunk, embeddedJson ? path : null, source.Syntax, target);
    }
    internal void Literal(TypedPath path, string text, string? role = null)
    {
        if (!Includes(role ?? source.SemanticRole)) return;
        var address = Address(path, role ?? source.SemanticRole); Charge(address, text);
        occurrences.Add(new(address, text, role ?? source.SemanticRole, AllowedEmpty: true));
    }
    internal void Key(TypedPath path, string key, string role = "translation.key", bool propertyName = false)
    {
        var address = Address(path, role, propertyName ? JsonTargetKind.PropertyName : JsonTargetKind.Value); Charge(address, key); keys.Add(new(address, key));
    }
    internal void Merge(TextExtractionResult result)
    {
        foreach (var text in result.Occurrences) if (Includes(text.Address.SemanticRole)) { Charge(text.Address, text.Text); occurrences.Add(text); }
        foreach (var key in result.TranslationKeys) { Charge(key.Address, key.Key); keys.Add(key); }
        foreach (var diagnostic in result.Diagnostics) { Charge(diagnostic.Address, diagnostic.Code); diagnostics.Add(diagnostic); }
        foreach (var command in result.PendingCommands) if (Includes(command.Address.SemanticRole)) { Charge(command.Address, command.Command); commands.Add(command); }
        foreach (var candidate in result.Unclassified) if (CouldInclude("command_storage")) { Charge(candidate.Address, candidate.Text); unclassified.Add(candidate); }
    }
    internal void Command(TypedPath path, string command)
    {
        if (!Includes("command.block")) return;
        var address = Address(path, "command.block"); Charge(address, command); commands.Add(new(address, command));
    }
    internal void RegisterComponent(SourceAddress address)
    {
        if (ComponentRoot is null) return;
        Charge(address, ""); ComponentRoot(address);
    }
    internal void Unclassified(TypedPath path, string text)
    {
        if (!CouldInclude("command_storage")) return;
        var address = Address(path, "command_storage.candidate"); Charge(address, text); unclassified.Add(new(address, text));
    }
    internal void Diagnostic(TypedPath path, string code)
    {
        var address = Address(path, source.SemanticRole); Charge(address, code); diagnostics.Add(new(address, code));
    }
    private void Charge(SourceAddress address, string text)
    {
        retainedBytes = checked(retainedBytes + TextExtractionBudget.RecordBytes(address, text.Length));
        if (retainedBytes > Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Extracted text/address retention budget exceeded");
    }
    internal TextExtractionResult Finish() => new(occurrences.ToImmutableArray(), keys.ToImmutableArray(), diagnostics.ToImmutableArray(), commands.ToImmutableArray(), unclassified.ToImmutableArray());
    internal static TypedPath At(TypedPath path, string key) => new(path.Segments.Add(PathSegment.ForKey(key)));
    internal static TypedPath At(TypedPath path, int index) => new(path.Segments.Add(PathSegment.ForIndex(index)));
}
