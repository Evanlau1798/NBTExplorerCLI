using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Minecraft;

public static class CommandTextExtractor
{
    public static CommandExtractionResult Extract(CommandCandidate source, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null, Action<CommandSelectorReference>? selectorReference = null, Action<CommandNameTemplate>? nameTemplate = null)
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
        return new CommandTextReader(source, profile, limits, scope ?? SnapshotScope.All, componentRoot, selectorReference, nameTemplate).Read();
    }
}
