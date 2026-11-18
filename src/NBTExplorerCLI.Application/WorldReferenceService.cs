using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using System.Collections.Immutable;

namespace NBTExplorerCLI.Application;

public sealed class WorldReferenceService(IWorldInventory inventory)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    public static bool IsIdentifyingName(string role) => role is "entity.name" or "armor_stand.name" or "entity.name.fallback" or "armor_stand.name.fallback";
    public ReferencePatch Create(WorldQuerySnapshot snapshot, string operationId, IEnumerable<LiteralTranslation> names)
    {
        var input = ReadNames(snapshot, names);
        return new(operationId, Build(snapshot, input.Index, input.Changes));
    }
    public ImmutableArray<ReferencePatch> CreateGroups(WorldQuerySnapshot snapshot, string operationPrefix, IEnumerable<LiteralTranslation> names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationPrefix); var input = ReadNames(snapshot, names);
        var groups = input.Changes.Keys.Select(address => input.Index.Parts[address].Target).Distinct()
            .GroupBy(target => (target.Type, Before: target.Name, After: Updated(target, input.Changes, snapshot.Limits.MaxDecodedBytes)))
            .OrderBy(group => group.Key.Type, StringComparer.Ordinal).ThenBy(group => group.Key.Before, StringComparer.Ordinal).ThenBy(group => group.Key.After, StringComparer.Ordinal).ToArray();
        ValidateFinalNames(snapshot, input.Index, input.Changes);
        var output = ImmutableArray.CreateBuilder<ReferencePatch>(); long retained = 0; var members = 0;
        foreach (var group in groups)
        {
            var synchronization = Build(snapshot, input.Index, group.SelectMany(target => target.Parts).Where(part => input.Changes.ContainsKey(part.Address))
                .ToDictionary(part => part.Address, part => input.Changes[part.Address]));
            Charge(synchronization, snapshot.Limits, ref retained, ref members);
            output.Add(new(operationPrefix + "-" + output.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), synchronization));
        }
        return output.ToImmutable();
    }
    private (NameIndex Index, Dictionary<SourceAddress, (string Before, string After)> Changes) ReadNames(WorldQuerySnapshot snapshot, IEnumerable<LiteralTranslation> names)
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
        var index = new NameIndex(new WorldReferenceScan(inventory, snapshot)); var changes = new Dictionary<SourceAddress, (string Before, string After)>();
        foreach (var item in requested)
        {
            if (!index.Parts.TryGetValue(item.Slot.Address, out var actual) || actual.Part.Text != item.Slot.SourceText || actual.Part.Context != item.Slot.Context || actual.Part.AllowedEmpty != item.Slot.AllowedEmpty
                || !changes.TryAdd(item.Slot.Address, (item.Slot.SourceText, item.Reply.Text))) WorldReferenceScan.Reject("A requested name differs from its exact source slot");
        }
        return (index, changes);
    }
    internal static ImmutableArray<ArtifactPrecondition> Validate(IWorldInventory inventory, WorldQuerySnapshot snapshot, IEnumerable<ReferencePatch> patches)
    {
        var requested = patches.ToArray(); if (requested.Length == 0) return [];
        var scan = new WorldReferenceScan(inventory, snapshot); var index = new NameIndex(scan);
        var allChanges = new Dictionary<SourceAddress, (string Before, string After)>(); long retained = 0; var members = 0;
        foreach (var patch in requested)
        {
            Charge(patch.Synchronization, snapshot.Limits, ref retained, ref members);
            var changes = patch.Synchronization.Members.Where(member => index.Parts.ContainsKey(member.Address))
                .ToDictionary(member => member.Address, member => (member.ExpectedText, member.ReplacementText));
            var expected = Build(snapshot, index, changes); var supplied = patch.Synchronization;
            if (expected.GroupId != supplied.GroupId || expected.GroupRevision != supplied.GroupRevision || expected.MemberDigest != supplied.MemberDigest || !expected.Members.SequenceEqual(supplied.Members))
                WorldReferenceScan.Reject("Reference synchronization is not the complete live typed group");
            foreach (var change in changes)
                if (!allChanges.TryAdd(change.Key, change.Value)) WorldReferenceScan.Reject("A name part belongs to multiple synchronization groups");
        }
        ValidateFinalNames(snapshot, index, allChanges);
        return scan.Inputs;
    }
    private static void Charge(ReferenceSynchronization group, DecodeLimits limits, ref long retained, ref int members)
    {
        foreach (var member in group.Members)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(member.Address, (long)member.ExpectedText.Length + member.ReplacementText.Length));
            if (++members > 100_000 || retained > limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Combined reference groups exceed the snapshot member budget");
        }
    }
    private sealed class NameIndex
    {
        internal WorldNamedTarget[] Targets { get; }
        internal Dictionary<SourceAddress, (WorldNamedTarget Target, TextOccurrence Part)> Parts { get; }
        internal ILookup<(string Type, string Name), WorldNamedTarget> ByName { get; }
        internal ILookup<string, WorldNameUse> Uses { get; }
        internal NameIndex(WorldReferenceScan scan)
        {
            Targets = scan.Targets.ToArray(); Parts = Targets.SelectMany(target => target.Parts.Select(part => (target, part))).ToDictionary(row => row.part.Address, row => (row.target, row.part));
            ByName = Targets.ToLookup(target => (target.Type, target.Name)); Uses = scan.Uses.ToLookup(use => use.Name, StringComparer.Ordinal);
        }
    }
    private static string Updated(WorldNamedTarget target, IReadOnlyDictionary<SourceAddress, (string Before, string After)> changes, long maximumBytes)
    {
        long characters = 0;
        foreach (var part in target.Parts) characters = checked(characters + (changes.TryGetValue(part.Address, out var value) ? value.After.Length : part.Text.Length));
        if (characters * 2 > maximumBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Composed name exceeds the snapshot budget");
        return string.Concat(target.Parts.Select(part => changes.TryGetValue(part.Address, out var value) ? value.After : part.Text));
    }
    private static void ValidateFinalNames(WorldQuerySnapshot snapshot, NameIndex index, IReadOnlyDictionary<SourceAddress, (string Before, string After)> changes)
    {
        var final = index.Targets.Select(target => (target.Type, Before: target.Name, After: Updated(target, changes, snapshot.Limits.MaxDecodedBytes)));
        if (final.GroupBy(target => (target.Type, target.After)).Any(group => group.Select(target => target.Before).Distinct(StringComparer.Ordinal).Skip(1).Any()))
            WorldReferenceScan.Reject("Distinct typed name groups cannot merge into one final identifier");
    }
    private static ReferenceSynchronization Build(WorldQuerySnapshot snapshot, NameIndex index, IReadOnlyDictionary<SourceAddress, (string Before, string After)> changes)
    {
        if (changes.Count == 0) WorldReferenceScan.Reject("Reference group has no explicitly named targets");
        if (changes.Any(change => !index.Parts.TryGetValue(change.Key, out var part) || part.Part.Text != change.Value.Before))
            WorldReferenceScan.Reject("Reference group name preimages differ");
        var targets = changes.Keys.Select(address => index.Parts[address].Target).Distinct().ToArray();
        var first = targets[0]; var type = first.Type; var oldName = first.Name;
        var newName = Updated(first, changes, snapshot.Limits.MaxDecodedBytes);
        if (targets.Any(target => target.Type != type || target.Name != oldName || Updated(target, changes, snapshot.Limits.MaxDecodedBytes) != newName)) WorldReferenceScan.Reject("A typed name group requires one consistent old and new identifier");
        var roots = targets.Select(target => target.Root).ToHashSet();
        var unselectedSameName = index.ByName[(type, oldName)].Any(target => !roots.Contains(target.Root));
        if (oldName != newName && index.ByName[(type, newName)].Any(target => !roots.Contains(target.Root))) WorldReferenceScan.Reject("The replacement identifier collides with another named target");
        var members = new List<ReferenceMember>(); long retained = 0;
        void Member(SourceAddress address, string before, string after, FormatFeature grammar)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, (long)before.Length + after.Length));
            if (retained > snapshot.Limits.MaxDecodedBytes || members.Count >= 100_000) throw new CodecException(CodecError.LimitExceeded, 0, "Reference members exceed the snapshot budget");
            members.Add(new(address, before, after, grammar.Schema, grammar.Version));
        }
        foreach (var target in targets)
            foreach (var part in target.Parts) Member(part.Address, part.Text, changes.TryGetValue(part.Address, out var value) ? value.After : part.Text, target.Grammar);
        foreach (var use in index.Uses[oldName].Concat(oldName == newName ? [] : index.Uses[newName]))
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
