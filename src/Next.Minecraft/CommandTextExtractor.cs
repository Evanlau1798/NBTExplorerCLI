using Next.Domain;

namespace Next.Minecraft;

public static class CommandTextExtractor
{
    public static CommandExtractionResult Extract(CommandCandidate source, CapabilityProfile profile, DecodeLimits? limits = null, SnapshotScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(profile);
        limits ??= DecodeLimits.Default; limits.Validate();
        var expected = MinecraftProfiles.ForDataVersion(profile.Id switch { "java-1.20.4" => 3700, "java-1.21" => 3953, "java-1.21.5" => 4325, "java-26.1-snapshot-6" => 4774, _ => 0 });
        if (expected is null || profile.Features[ProfileFacet.CommandGrammar] != expected.Features[ProfileFacet.CommandGrammar])
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Unknown command grammar");
        if (source.Address.Syntax is { } syntax && (syntax.GrammarProfile != expected.Features[ProfileFacet.CommandGrammar].Schema || syntax.GrammarVersion != expected.Features[ProfileFacet.CommandGrammar].Version))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Command grammar differs from its source binding");
        if (source.Address.Syntax is { Encoding: not ("utf8" or "utf8-bom") }) throw new CodecException(CodecError.UnsupportedProfile, 0, "Unsupported command source encoding");
        return new CommandTextReader(source, profile, limits, scope ?? SnapshotScope.All).Read();
    }
}
