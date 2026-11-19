using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

internal static class SessionPageBuilder
{
    internal static ImmutableArray<ImmutableArray<Hash256>> Build(SessionStartRequest request)
    {
        var formats = (request.Formats.IsDefault ? [] : request.Formats).ToDictionary(item => item.SlotId, item => item.Format);
        var slots = request.Slots.Select(slot => new SessionPageSlot(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty,
            slot.Placeholders.Tokens.Order(StringComparer.Ordinal).ToImmutableArray(), formats.GetValueOrDefault(slot.Id.Digest), slot.Placeholders.ProtectVisibleText)).ToArray();
        Dictionary<Hash256, SessionContextGroup>? context = null;
        if (request.Policy.GroupRelatedText)
        {
            context = SessionContextGrouping.Build(request.Slots, request.Source.ContextRoots).SelectMany(group => group.Slots.Select(id => (id, group)))
                .ToDictionary(item => item.id, item => item.group);
            var byId = slots.ToDictionary(slot => slot.SlotId);
            slots = slots.Select(slot => context[slot.SlotId]).DistinctBy(group => group.GroupId.Digest).SelectMany(group => group.Slots.Select(id => byId[id])).ToArray();
        }
        var glyphSlots = request.Slots.Where(slot => slot.Placeholders.ProtectPrivateUseGlyphs).Select(slot => slot.Id.Digest).ToHashSet();
        var pages = ImmutableArray.CreateBuilder<ImmutableArray<Hash256>>(); var offset = 0;
        while (offset < slots.Length)
        {
            var low = 1; var high = Math.Min(request.Policy.MaximumPageSlots, slots.Length - offset); var best = 0;
            while (low <= high)
            {
                var count = low + (high - low) / 2;
                var members = slots.AsSpan(offset, count).ToArray().ToImmutableArray();
                var policyVersion = SessionPage.PolicyFor(members.Any(slot => glyphSlots.Contains(slot.SlotId)));
                var page = new SessionPage(request.SessionId, long.MaxValue, request.Source.SnapshotId.Digest, pages.Count, Hash256.Compute([]), request.SourceLocale, request.TargetLocale,
                    policyVersion, request.Policy.PreservePlaceholderOrder, members);
                var fits = false;
                try { fits = Encoding.UTF8.GetByteCount(SessionExchangeJson.Prompt(SessionExchangeJson.SealPage(page), Guid.NewGuid(), context)) <= request.Policy.MaximumPageBytes; }
                catch (JsonException) { }
                if (fits) { best = count; low = count + 1; } else high = count - 1;
            }
            if (best == 0) throw new SessionPageOversizedException(slots[offset].SlotId);
            if (context is not null && offset + best < slots.Length)
            {
                var end = offset + best;
                while (end > offset && context[slots[end - 1].SlotId] == context[slots[end].SlotId]) end--;
                if (end > offset) best = end - offset;
            }
            pages.Add(slots.AsSpan(offset, best).ToArray().Select(slot => slot.SlotId).ToImmutableArray()); offset += best;
        }
        return pages.ToImmutable();
    }
}

public sealed class SessionPageOversizedException(Hash256 slotId) : Exception("A single slot exceeds the serialized page budget")
{
    public Hash256 SlotId { get; } = slotId;
}
