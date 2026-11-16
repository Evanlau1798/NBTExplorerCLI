using Next.Domain;
using Next.Minecraft;
using System.Collections.Immutable;

namespace Next.Application;

public sealed class WorldReferenceService(IWorldInventory inventory)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    public ReferencePatch Create(WorldQuerySnapshot snapshot, string operationId, IEnumerable<LiteralTranslation> names)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(names);
        var requested = new List<LiteralTranslation>(); long retained = 0;
        foreach (var item in names)
        {
            if (requested.Count >= 100_000 || item?.Slot is not { } slot || item.Reply?.Text is not { } text || item.Reply.SlotId is null || item.Reply.TargetLocale is null) throw new ArgumentException("Invalid bounded name translation set", nameof(names));
            var characters = (long)slot.SourceText.Length + text.Length + slot.Context.Length + slot.SourceLocale.Length + slot.TargetLocale.Length + item.Reply.TargetLocale.Length + slot.Placeholders.Tokens.Sum(token => (long)token.Length)
                + (slot.Address.Syntax?.Encoding.Length ?? 0);
            // Canonical UTF-16 bytes also bound the retained address strings; syntax encoding is outside identity.
            retained = checked(retained + TextExtractionBudget.RecordBytes(slot.Address, characters) + 2L * slot.Id.Canonical.Length + item.Reply.SlotId.Canonical.Length);
            if (retained > snapshot.Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Name translation inputs exceed the snapshot budget");
            if (slot.Validate(item.Reply) != SlotValidation.Valid) throw new ArgumentException("Invalid name translation", nameof(names));
            requested.Add(item);
        }
        if (requested.Count == 0) throw new ArgumentException("Empty name translation set", nameof(names));
        var scan = new WorldReferenceScan(inventory, snapshot); var changes = new Dictionary<SourceAddress, (string Before, string After)>();
        var parts = scan.Targets.SelectMany(target => target.Parts).ToDictionary(part => part.Address);
        foreach (var item in requested)
        {
            if (!parts.TryGetValue(item.Slot.Address, out var actual) || actual.Text != item.Slot.SourceText || actual.Context != item.Slot.Context || actual.AllowedEmpty != item.Slot.AllowedEmpty
                || !changes.TryAdd(item.Slot.Address, (item.Slot.SourceText, item.Reply.Text))) WorldReferenceScan.Reject("A requested name differs from its exact source slot");
        }
        return new(operationId, Build(snapshot, scan, changes));
    }
    internal static ImmutableArray<ArtifactPrecondition> Validate(IWorldInventory inventory, WorldQuerySnapshot snapshot, IEnumerable<ReferencePatch> patches)
    {
        var requested = patches.ToArray(); if (requested.Length == 0) return [];
        var scan = new WorldReferenceScan(inventory, snapshot);
        var names = scan.Targets.SelectMany(target => target.Parts).ToDictionary(part => part.Address);
        foreach (var patch in requested)
        {
            var changes = patch.Synchronization.Members.Where(member => names.ContainsKey(member.Address))
                .ToDictionary(member => member.Address, member => (member.ExpectedText, member.ReplacementText));
            var expected = Build(snapshot, scan, changes); var supplied = patch.Synchronization;
            if (expected.GroupId != supplied.GroupId || expected.GroupRevision != supplied.GroupRevision || expected.MemberDigest != supplied.MemberDigest || !expected.Members.SequenceEqual(supplied.Members))
                WorldReferenceScan.Reject("Reference synchronization is not the complete live typed group");
        }
        return scan.Inputs;
    }
    private static ReferenceSynchronization Build(WorldQuerySnapshot snapshot, WorldReferenceScan scan, IReadOnlyDictionary<SourceAddress, (string Before, string After)> changes)
    {
        if (changes.Count == 0) WorldReferenceScan.Reject("Reference group has no explicitly named targets");
        var actualParts = scan.Targets.SelectMany(target => target.Parts).ToDictionary(part => part.Address);
        var targets = scan.Targets.Where(target => target.Parts.Any(part => changes.ContainsKey(part.Address))).ToArray();
        if (targets.Length == 0 || changes.Any(change => !actualParts.TryGetValue(change.Key, out var part) || part.Text != change.Value.Before))
            WorldReferenceScan.Reject("Reference group name preimages differ");
        var first = targets[0]; var type = first.Type; var oldName = first.Name;
        string Updated(WorldNamedTarget target)
        {
            long characters = 0;
            foreach (var part in target.Parts) characters = checked(characters + (changes.TryGetValue(part.Address, out var value) ? value.After.Length : part.Text.Length));
            if (characters * 2 > snapshot.Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Composed name exceeds the snapshot budget");
            return string.Concat(target.Parts.Select(part => changes.TryGetValue(part.Address, out var value) ? value.After : part.Text));
        }
        var newName = Updated(first);
        if (targets.Any(target => target.Type != type || target.Name != oldName || Updated(target) != newName)) WorldReferenceScan.Reject("A typed name group requires one consistent old and new identifier");
        var roots = targets.Select(target => target.Root).ToHashSet();
        var unselectedSameName = scan.Targets.Any(target => target.Type == type && target.Name == oldName && !roots.Contains(target.Root));
        if (oldName != newName && scan.Targets.Any(target => target.Type == type && target.Name == newName && !roots.Contains(target.Root))) WorldReferenceScan.Reject("The replacement identifier collides with another named target");
        var members = new List<ReferenceMember>(); long retained = 0;
        void Member(SourceAddress address, string before, string after, FormatFeature grammar)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, (long)before.Length + after.Length));
            if (retained > snapshot.Limits.MaxDecodedBytes || members.Count >= 100_000) throw new CodecException(CodecError.LimitExceeded, 0, "Reference members exceed the snapshot budget");
            members.Add(new(address, before, after, grammar.Schema, grammar.Version));
        }
        foreach (var target in targets)
            foreach (var part in target.Parts) Member(part.Address, part.Text, changes.TryGetValue(part.Address, out var value) ? value.After : part.Text, target.Grammar);
        foreach (var use in scan.Uses)
        {
            if (use.Name != oldName && use.Name != newName || use.Kind is 'a' or 'p' or 'r') continue;
            if (use.Kind is not ('e' or 'n') || use.Type is null) WorldReferenceScan.Reject("Reference target domain is not proven independently of players or execution context");
            if (use.Type != type) continue;
            if (use.Name != oldName) WorldReferenceScan.Reject("An existing reference already uses the replacement identifier");
            if (unselectedSameName) WorldReferenceScan.Reject("The selector can address unselected same-name targets");
            if (!use.Writable) WorldReferenceScan.Reject("Encoded command reference rewriting is not yet authorized by its grammar");
            Member(use.Address, use.Direct ? use.Name : use.Selector, use.Direct ? newName : ReplaceSelector(use.Selector, newName, snapshot.Limits), use.Grammar);
        }
        var ordered = members.OrderBy(member => member.Occurrence.Digest.Hex, StringComparer.Ordinal).ToArray();
        var anchor = targets.OrderBy(target => SourceIdentity.Occurrence(target.Root).Digest.Hex, StringComparer.Ordinal).First().Root;
        var group = SourceIdentity.Group("world.name-sync-v1." + snapshot.Manifest.SnapshotId.Digest.Hex, anchor, 1);
        return new(group, 0, SourceIdentity.Members(group, ordered.Select(member => member.Occurrence), 0).Digest, ReferenceCompleteness.Complete, ordered);
    }
    private static string ReplaceSelector(string source, string name, DecodeLimits limits)
    {
        var parsed = SelectorNames.Parse(source, limits); var token = parsed.Names.Single();
        var characters = checked((long)source.Length - token.Length + 2 + name.Length + name.Count(unit => unit is '\\' or '"'));
        if (characters * 2 > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Selector replacement exceeds the snapshot budget");
        var quoted = "\"" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return source[..token.Start] + quoted + source[(token.Start + token.Length)..];
    }
}

internal static class WorldReferenceMembers
{
    internal static LiteralTranslation[] Literals(IEnumerable<PatchOperation> operations, ArtifactAddress artifact, ChunkPosition? chunk = null)
        => operations.OfType<ReferencePatch>().SelectMany(patch => patch.Synchronization.Members).Where(member => member.Address.Artifact == artifact && member.Address.Chunk == chunk).Select(member =>
        {
            var slot = new TextOccurrence(member.Address, member.ExpectedText, member.Address.SemanticRole, true).ToSlot("en_us", "en_us");
            return new LiteralTranslation(slot, new(slot.Id, slot.SourceTextHash, "en_us", member.ReplacementText));
        }).ToArray();
    internal static void Catalog(IWorldInventory inventory, PatchPlan plan)
    {
        if (!plan.Operations.Any(operation => operation is ReferencePatch)) return;
        var discovery = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
        var known = plan.Preconditions.Select(input => input.Artifact).ToHashSet();
        if (!discovery.Complete || WorldReferenceScan.Relevant(discovery).Any(source => !known.Contains(PlanOuter(source.Location.Artifact))))
        {
            var details = discovery.Sources.Where(source => source.Outcome is not (SourceOutcome.Success or SourceOutcome.Irrelevant) || source.Outcome == SourceOutcome.Success && !known.Contains(PlanOuter(source.Location.Artifact)))
                .Take(3).Select(source => source.Location.Artifact.RelativePath + ":" + source.Outcome + ":" + source.DiagnosticCode);
            throw new ArtifactConflictException("The complete reference source catalog changed before world commit: " + discovery.DiagnosticCode + " " + string.Join(",", details));
        }
    }
    private static ArtifactAddress PlanOuter(ArtifactAddress artifact) => artifact.ContainerEntry is null ? artifact : new(artifact.WorldId, artifact.RelativePath, artifact.Kind);
}
