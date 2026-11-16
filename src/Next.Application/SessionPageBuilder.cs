using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Formats;

namespace Next.Application;

internal static class SessionPageBuilder
{
    internal static ImmutableArray<ImmutableArray<Hash256>> Build(SessionStartRequest request)
    {
        var formats = (request.Formats.IsDefault ? [] : request.Formats).ToDictionary(item => item.SlotId, item => item.Format);
        var slots = request.Slots.Select(slot => new SessionPageSlot(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens.Order(StringComparer.Ordinal).ToImmutableArray(), formats.GetValueOrDefault(slot.Id.Digest))).ToArray();
        var pages = ImmutableArray.CreateBuilder<ImmutableArray<Hash256>>(); var offset = 0;
        while (offset < slots.Length)
        {
            var low = 1; var high = Math.Min(request.Policy.MaximumPageSlots, slots.Length - offset); var best = 0;
            while (low <= high)
            {
                var count = low + (high - low) / 2;
                var page = new SessionPage(request.SessionId, long.MaxValue, request.Source.SnapshotId.Digest, pages.Count, Hash256.Compute([]), request.SourceLocale, request.TargetLocale, "literal-v1", request.Policy.PreservePlaceholderOrder, slots.AsSpan(offset, count).ToArray().ToImmutableArray());
                var fits = false;
                try { fits = Encoding.UTF8.GetByteCount(SessionExchangeJson.Prompt(SessionExchangeJson.SealPage(page), Guid.NewGuid())) <= request.Policy.MaximumPageBytes; }
                catch (JsonException) { }
                if (fits) { best = count; low = count + 1; } else high = count - 1;
            }
            if (best == 0) throw new SessionPageOversizedException(slots[offset].SlotId);
            pages.Add(slots.AsSpan(offset, best).ToArray().Select(slot => slot.SlotId).ToImmutableArray()); offset += best;
        }
        return pages.ToImmutable();
    }
}

public sealed class SessionPageOversizedException(Hash256 slotId) : Exception("A single slot exceeds the serialized page budget")
{
    public Hash256 SlotId { get; } = slotId;
}
