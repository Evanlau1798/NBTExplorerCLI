using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal static class WorldStorageSignSafety
{
    internal static bool Targets(IEnumerable<PatchOperation> operations) => operations.Any(operation => operation switch
    {
        LiteralPatch literal => SignLine(literal.Translation.Slot.Address)
            && literal.Translation.Slot.SourceText != literal.Translation.Reply.Text,
        KeyConversionPatch conversion => SignLine(conversion.Conversion.Slot.Address),
        _ => false
    });

    private static bool SignLine(SourceAddress address) => address.SemanticRole is "sign.front.line" or "sign.back.line"
        or "hanging_sign.front.line" or "hanging_sign.back.line";

    internal static WorldDiscoveryResult Bind(IWorldInventory inventory, DecodeLimits limits, string? classicVersion)
    {
        // Selection narrows the edits. A comparison may consume the same sign from a different function.
        var extraction = new WorldTextExtraction(inventory, new(SnapshotScope.All, limits, ClassicVersion: classicVersion));
        var discovery = extraction.Discovery;
        if (!discovery.Complete || discovery.Metadata?.DataVersion is >= MinecraftProfiles.CommandStorageDataVersion
            && extraction.ReadSources().Any(row => row.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled
                || row.Outcome == SourceOutcome.Unsupported && (row.Source.MayUseCommandStorage || row.Commands is not null
                    || row.Text is { PendingCommands.IsEmpty: false })))
            throw new EditRejectedException(EditFailure.Precondition, "Translating signs requires complete analysis of possible storage comparisons");
        return discovery;
    }
}
