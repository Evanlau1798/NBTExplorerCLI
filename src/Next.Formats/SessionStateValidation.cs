using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Next.Domain;

namespace Next.Formats;

internal static class SessionStateValidation
{
    internal static void Start(SessionStartRequest request)
    {
        if (request is null || request.SessionId == Guid.Empty || request.Source is null || request.Policy is null || string.IsNullOrWhiteSpace(request.SourceLocale)
            || string.IsNullOrWhiteSpace(request.TargetLocale) || request.Slots.IsDefault || request.Slots.Length is < 1 or > 100_000
            || request.Policy.MaximumPageSlots is < 1 or > 100_000 || request.Policy.MaximumPageBytes is < 1024 or > 67_108_864) Invalid();
        var source = request!.Source;
        if (source.WorldId == Guid.Empty || source.SnapshotId is null || source.Versions is null || source.Sources.IsDefault || source.Sources.Length > 100_000
            || source.Dimensions.IsDefault || source.Regions.IsDefault || source.Types.IsDefault || source.ArtifactPaths.IsDefault
            || source.Sources.Any(item => item is null || item.Artifact is null || item.Artifact.ContainerEntry is not null)
            || source.Sources.Select(item => item.Artifact.RelativePath).Distinct(StringComparer.Ordinal).Count() != source.Sources.Length) Invalid();
        try
        {
            if (SourceIdentity.Snapshot(source.WorldId, source.Sources, new(source.Dimensions, source.Regions, source.Types, source.ArtifactPaths), source.Versions) != source.SnapshotId) Invalid();
        }
        catch (ArgumentException error) { throw new JsonException("Invalid session source snapshot", error); }
        var raw = source.Sources.ToDictionary(item => item.Artifact.RelativePath, StringComparer.Ordinal); var ids = new HashSet<Hash256>();
        foreach (var slot in request.Slots)
            if (slot is null || slot.Address.Artifact.WorldId != source.WorldId || !ids.Add(slot.Id.Digest) || slot.SourceLocale != request.SourceLocale || slot.TargetLocale != request.TargetLocale
                || !raw.TryGetValue(slot.Address.Artifact.RelativePath, out var input) || input.ContentHash is null || input.Artifact.Kind != slot.Address.Artifact.Kind) Invalid();
        if (!source.ContextRoots.IsDefault)
        {
            if (source.ContextRoots.Length > request.Slots.Length) Invalid();
            var byId = request.Slots.ToDictionary(slot => slot.Id.Digest); var rooted = new HashSet<Hash256>();
            foreach (var root in source.ContextRoots)
            {
                if (root is null || root.SlotId is null || root.Container is null || !rooted.Add(root.SlotId) || !byId.TryGetValue(root.SlotId, out var slot)
                    || !slot.Role.StartsWith("command_storage.component", StringComparison.Ordinal) || root.Container.Artifact != slot.Address.Artifact
                    || root.Container.Dimension != slot.Address.Dimension || root.Container.Chunk != slot.Address.Chunk || root.Container.ComponentPath is not null || root.Container.Syntax is not null
                    || root.Container.JsonTarget != JsonTargetKind.Value || root.Container.SemanticRole != "command_storage.component"
                    || root.Container.NodePath.Segments.Length > slot.Address.NodePath.Segments.Length || !root.Container.NodePath.Segments.SequenceEqual(slot.Address.NodePath.Segments.Take(root.Container.NodePath.Segments.Length))) Invalid();
            }
        }
        if (!request.Formats.IsDefault)
        {
            var formats = new HashSet<Hash256>();
            if (request.Formats.Length > 100_000 || request.Formats.Any(item => item is null || item.SlotId is null || !ids.Contains(item.SlotId) || !formats.Add(item.SlotId) || !Enum.IsDefined(item.Format))) Invalid();
            var slots = request.Slots.ToDictionary(item => item.Id.Digest);
            foreach (var item in request.Formats)
                if (item.Format != PlaceholderFormat.Literal)
                {
                    try { _ = PercentPlaceholders.Equivalent(slots[item.SlotId].SourceText, slots[item.SlotId].SourceText, item.Format == PlaceholderFormat.NormalizedPercent); }
                    catch (PlaceholderLimitException error) { throw new JsonException("Source placeholder budget exceeded", error); }
                }
        }
    }
    internal static void State(TranslationSession session)
    {
        if (session is null || session.Revision < 0 || session.Slots.IsDefault || session.Slots.Any(slot => slot is null || slot.Slot is null)
            || session.Pages.IsDefault || session.Pages.Length is < 1 or > 100_000 || session.Operations.IsDefault || session.Operations.Length is < 1 or > 100_000
            || session.Revision != session.Operations.Length - 1) Invalid();
        var declared = session!.Slots.Where(item => item.Format != PlaceholderFormat.Literal).Select(item => new SessionSlotFormat(item.Slot.Id.Digest, item.Format)).ToImmutableArray();
        Start(new(session.SessionId, session.Source, session.SourceLocale, session.TargetLocale, session.Policy, session.Slots.Select(item => item.Slot).ToImmutableArray(), declared));
        var slots = session.Slots.ToDictionary(item => item.Slot.Id.Digest, item => item.Slot); var members = new HashSet<Hash256>();
        var formatsById = declared.ToDictionary(item => item.SlotId, item => item.Format);
        foreach (var page in session.Pages)
        {
            if (page.IsDefault || page.Length is < 1 || page.Length > session.Policy.MaximumPageSlots) Invalid();
            foreach (var id in page) if (id is null || !slots.ContainsKey(id) || !members.Add(id)) Invalid();
        }
        if (members.Count != slots.Count) Invalid();
        var operations = new HashSet<Guid>(); var current = new Dictionary<Hash256, (SessionSlotState State, SlotReply? Reply)>();
        SessionWorkspace? workspace = null;
        for (var index = 0; index < session.Operations.Length; index++)
        {
            var operation = session.Operations[index];
            if (operation is null || operation.OperationId == Guid.Empty || !operations.Add(operation.OperationId) || operation.InputHash is null || operation.Revision != index
                || operation.FromRevision != index - 1 || operation.Slots.IsDefault || operation.Slots.Any(id => id is null || !slots.ContainsKey(id))
                || operation.Slots.Distinct().Count() != operation.Slots.Length) Invalid();
            if (index == 0)
            {
                var start = new SessionStartRequest(session.SessionId, session.Source, session.SourceLocale, session.TargetLocale, session.Policy, session.Slots.Select(item => item.Slot).ToImmutableArray(), declared);
                var hash = Hash256.Compute(Encoding.UTF8.GetBytes(TranslationSessionJson.WriteStart(start)));
                if (operation!.Kind != "start" || operation.InputBase64 is not null || operation.Slots.Length != slots.Count
                    || operation.InputHash != hash) Invalid();
                continue;
            }
            if (operation!.Kind is not ("reply" or "command") || operation.InputBase64 is null || operation.InputBase64.Length > 89_478_488) Invalid();
            byte[] bytes;
            try { bytes = Convert.FromBase64String(operation.InputBase64!); } catch (FormatException error) { throw new JsonException("Invalid retained reply", error); }
            if (Hash256.Compute(bytes) != operation.InputHash) Invalid();
            if (operation.Kind == "command")
            {
                var command = SessionCommandJson.Read(new UTF8Encoding(false, true).GetString(bytes));
                if (command.OperationId != operation.OperationId || !operation.Slots.ToHashSet().SetEquals(command.Kind == SessionCommandKind.Reset ? command.ResetSlots : [])) Invalid();
                var replaySlots = session.Slots.Select(item =>
                {
                    var value = current.GetValueOrDefault(item.Slot.Id.Digest, (SessionSlotState.Pending, null));
                    return item with { State = value.Item1, Reply = value.Item2, Reason = null };
                }).ToImmutableArray();
                TranslationSession changed;
                try { changed = SessionWorkspaceCommands.Apply(session with { Revision = operation.FromRevision, Slots = replaySlots, Workspace = workspace }, command); }
                catch (ArgumentException error) { throw new JsonException("Invalid retained session command", error); }
                workspace = changed.Workspace;
                foreach (var item in changed.Slots) current[item.Slot.Id.Digest] = (item.State, item.Reply);
                continue;
            }
            var reply = SessionExchangeJson.ReadReply(new UTF8Encoding(false, true).GetString(bytes));
            if (reply.OperationId != operation.OperationId || reply.Revision != operation.FromRevision || reply.Page < 0 || reply.Page >= session.Pages.Length
                || !operation.Slots.ToHashSet().SetEquals(reply.Slots.Select(item => item.SlotId))) Invalid();
            var page = SessionPageProjection.Create(session, reply.Page, reply.Revision, slots, formatsById);
            var validated = SessionReplyValidator.Validate(page, page.Slots.Select(item => slots[item.SlotId]), reply, formatsById);
            if (!validated.Valid) Invalid();
            foreach (var translated in validated.Value!.Translations) current[translated.Slot.Id.Digest] = (SessionSlotState.Translated, translated.Reply);
            foreach (var unchanged in validated.Value.UnchangedSlots) current[unchanged] = (SessionSlotState.Unchanged, null);
        }
        if (ContractJson.Write("nbtcli.next.session-workspace", new { workspace }) != ContractJson.Write("nbtcli.next.session-workspace", new { workspace = session.Workspace })) Invalid();
        foreach (var item in session.Slots)
        {
            var expected = current.GetValueOrDefault(item.Slot.Id.Digest, (SessionSlotState.Pending, null));
            if (item.State != expected.Item1 || item.Reply != expected.Item2 || item.Reason is not null) Invalid();
        }
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new JsonException("Invalid translation session state");
}
