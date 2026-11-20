using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public enum CommandStorageUse { InterpretedDisplay, LiteralDisplay, ListElementWrite, Other, ListReplacement, ListCopy, Presence, EquipmentImport, EquipmentExport, ListCardinality, ScalarWrite, ScratchComparison, IndexedBlankWrite, ScratchControlComparison }
public readonly record struct StorageGlyphLiteral(string Font, string Text);
public sealed record CommandStorageAccess(string Storage, string Path, CommandStorageUse Use, SourceAddress Address, bool HasUnclassifiedReferences = false)
{
    public NbtType? ListElementType { get; init; }
    public (string Storage, string Path)? CopySource { get; init; }
    public (string Storage, string Path)? DisplayContainer { get; init; }
    public SourceAddress? BlankResetAddress { get; init; }
    public SourceAddress? ControlResetAddress { get; init; }
    public bool ReplacesList { get; init; }
    public bool AppendsScalar { get; init; }
    public IEnumerable<StorageGlyphLiteral>? GlyphLiterals { get; init; }
    public StorageAttributeOperation? AttributeOperation { get; init; }
}
public sealed record CommandFunctionCall(string Target, bool ResultObserved);

public static class CommandTextExtractor
{
    public static CommandExtractionResult Extract(CommandCandidate source, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null, Action<CommandSelectorReference>? selectorReference = null, Action<CommandNameTemplate>? nameTemplate = null, Action<CommandStorageAccess>? storageAccess = null, IReadOnlySet<(string Storage, string Path)>? storageDisplayLists = null, int? runtimeDataVersion = null)
        => Extract(source, profile, limits, scope, componentRoot, selectorReference, nameTemplate, storageAccess, storageDisplayLists, runtimeDataVersion, null);

    public static CommandExtractionResult Extract(CommandCandidate source, CapabilityProfile profile, DecodeLimits? limits, SnapshotScope? scope, Action<SourceAddress>? componentRoot, Action<CommandSelectorReference>? selectorReference, Action<CommandNameTemplate>? nameTemplate, Action<CommandStorageAccess>? storageAccess, IReadOnlySet<(string Storage, string Path)>? storageDisplayLists, int? runtimeDataVersion, Action? opaqueEquipment)
        => Extract(source, profile, limits, scope, componentRoot, selectorReference, nameTemplate, storageAccess, storageDisplayLists, runtimeDataVersion, opaqueEquipment, null);

    public static CommandExtractionResult Extract(CommandCandidate source, CapabilityProfile profile, DecodeLimits? limits, SnapshotScope? scope, Action<SourceAddress>? componentRoot, Action<CommandSelectorReference>? selectorReference, Action<CommandNameTemplate>? nameTemplate, Action<CommandStorageAccess>? storageAccess, IReadOnlySet<(string Storage, string Path)>? storageDisplayLists, int? runtimeDataVersion, Action? opaqueEquipment, Action<CommandFunctionCall>? functionCall)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(profile);
        using var observation = WorkDiagnostics.Start("command.parse", source.Address.Artifact.RelativePath, worldId: source.Address.Artifact.WorldId);
        limits ??= DecodeLimits.Default; limits.Validate();
        var expected = MinecraftProfiles.ForId(profile.Id);
        if (expected is null || profile.Features[ProfileFacet.CommandGrammar] != expected.Features[ProfileFacet.CommandGrammar])
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown command grammar");
        if (source.Address.Syntax is { } syntax && (syntax.GrammarProfile != expected.Features[ProfileFacet.CommandGrammar].Schema || syntax.GrammarVersion != expected.Features[ProfileFacet.CommandGrammar].Version))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Command grammar differs from its source binding");
        if (source.Address.Syntax is { Encoding: not ("utf8" or "utf8-bom") }) throw new CodecException(CodecError.UnsupportedProfile, 0, "Unsupported command source encoding");
        return new CommandTextReader(source, profile, limits, scope ?? SnapshotScope.All, componentRoot, selectorReference, nameTemplate,
            storageAccess: storageAccess, storageDisplayLists: storageDisplayLists, runtimeDataVersion: runtimeDataVersion, opaqueEquipment: opaqueEquipment, functionCall: functionCall).Read();
    }
}
