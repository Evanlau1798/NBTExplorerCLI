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
        TypedPatch typed => MayEditSign(typed),
        ReferencePatch reference => reference.Synchronization.Members.Any(member => member.ExpectedText != member.ReplacementText
            && (NativeSignPath(member.Address.Artifact, member.Address.NodePath.Segments.AsSpan())
                || member.Address.Syntax?.NodePath?.Segments.Any(segment => segment.Key is "front_text" or "back_text") == true)),
        _ => false
    });

    private static bool SignLine(SourceAddress address) => address.SemanticRole is "sign.front.line" or "sign.back.line"
        or "hanging_sign.front.line" or "hanging_sign.back.line";

    private static bool MayEditSign(TypedPatch patch)
    {
        var edit = patch.Edit;
        if (edit.Action == TypedEditAction.Replace && edit.Expected.Equals(edit.Replacement)) return false;
        var path = edit.Path.Segments;
        if (edit.Action == TypedEditAction.Add) path = path.Add(edit.Child!);
        return NativeSignPath(patch.Address.Artifact, path.AsSpan())
            || edit.Action == TypedEditAction.Rename
            && NativeSignPath(patch.Address.Artifact, path.SetItem(path.Length - 1, PathSegment.ForKey(edit.NewName!)).AsSpan());
    }

    private static bool NativeSignPath(ArtifactAddress artifact, ReadOnlySpan<PathSegment> path)
    {
        if (artifact.Kind == ArtifactKind.Nbt && artifact.RelativePath.EndsWith(".nbt", StringComparison.OrdinalIgnoreCase))
            return path.IsEmpty || path[0].Key is "blocks" or "palette" or "palettes";
        if (artifact.Kind != ArtifactKind.Region) return false;
        if (path.IsEmpty) return true;
        if (path[0].Key == "Level")
        {
            path = path[1..];
            if (path.IsEmpty) return true;
        }
        return (path[0].Key is "block_entities" or "TileEntities")
            && (path.Length < 3 || path[2].Key is "id" or "x" or "y" or "z" or "front_text" or "back_text"
                or "Text1" or "Text2" or "Text3" or "Text4" or "FilteredText1" or "FilteredText2" or "FilteredText3" or "FilteredText4");
    }

    internal static WorldDiscoveryResult Bind(IWorldInventory inventory, DecodeLimits limits, string? classicVersion)
    {
        // Selection narrows the edits. A comparison may consume the same sign from a different function.
        var extraction = new WorldTextExtraction(inventory, new(SnapshotScope.All, limits, ClassicVersion: classicVersion));
        var discovery = extraction.Discovery;
        long retained = 0;
        var results = new StorageFunctionResults(Charge);
        var imports = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var opaque = false;
        var classicComparison = false;
        if (!discovery.Complete || extraction.ReadSignDependencies(results.ObserveCall, ObserveEffect, ObserveStorage, () => opaque = true)
                .Any(row => row.Outcome is SourceOutcome.Failed or SourceOutcome.Cancelled
                    || row.Outcome == SourceOutcome.Unsupported && (row.Source.MayUseCommandStorage || row.Commands is not null
                        || row.Text is { PendingCommands.IsEmpty: false }))
            || classicComparison || imports.Count != 0 && (opaque || ImportsObserved())
            || results.HasObservedSignEffects(discovery, CancellationToken.None))
            throw new EditRejectedException(EditFailure.Precondition, "Changing signs requires complete analysis of observed command results and storage comparisons");
        return discovery;

        void Charge(int characters)
        {
            retained = checked(retained + 256L + 2L * characters);
            if (retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Sign function dependencies exceed the snapshot budget");
        }

        void ObserveEffect(SourceAddress address, CommandSignEffect effect)
        {
            if (effect == CommandSignEffect.ComparesClassicText) classicComparison = true;
            else results.ObserveSign(address, effect);
        }

        void ObserveStorage(CommandStorageAccess access)
        {
            if (access.Use == CommandStorageUse.SignImport)
            {
                Add(imports, access.Storage, access.Path);
                if (access.Address.SemanticRole == "command.function") results.ObserveSign(access.Address, CommandSignEffect.MutatesText);
            }
            else if (access.Use != CommandStorageUse.Presence) Add(uses, access.Storage, access.Path);
            if (access.CopySource is { } from) Add(uses, from.Storage, from.Path);
        }

        void Add(Dictionary<string, HashSet<string>> index, string storage, string path)
        {
            if (!index.TryGetValue(storage, out var paths)) index.Add(storage, paths = new(StringComparer.Ordinal));
            path = WorldStorageUseIndex.PlainAncestor(path);
            if (paths.Add(path)) Charge(storage.Length + path.Length);
        }

        bool ImportsObserved()
        {
            foreach (var (storage, paths) in imports)
            {
                if (!uses.TryGetValue(storage, out var readers)) continue;
                if (paths.Contains("") || readers.Contains("")) return true;
                var ordered = readers.Order(StringComparer.Ordinal).ToArray();
                if (paths.Any(path => readers.Contains(path) || WorldStorageUseIndex.RelatedPath(path, readers, ordered))) return true;
            }
            return false;
        }
    }
}
