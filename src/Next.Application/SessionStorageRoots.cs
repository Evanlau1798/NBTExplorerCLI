using System.Collections.Immutable;
using Next.Domain;

namespace Next.Application;

internal static class SessionStorageRoots
{
    internal static ImmutableArray<SessionGroupRoot> Build(WorldQuerySnapshot snapshot, IEnumerable<TextSlot> slots)
    {
        var roots = ImmutableArray.CreateBuilder<SessionGroupRoot>();
        foreach (var slot in slots.Where(slot => slot.Role.StartsWith("command_storage.component", StringComparison.Ordinal)))
        {
            if (!snapshot.StorageRules.TryGetValue(slot.Address.Artifact, out var rules)) throw new InvalidDataException("Storage component grouping requires its explicit scan rule");
            var matches = rules.Where(rule => rule.Kind == StorageTextKind.TextComponent && Prefix(rule.Path, slot.Address.NodePath)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Storage component grouping root is missing or ambiguous");
            roots.Add(new(slot.Id.Digest, new(slot.Address.Artifact, matches[0].Path, "command_storage.component", slot.Address.Dimension, slot.Address.Chunk)));
        }
        return roots.Count == 0 ? default : roots.OrderBy(root => root.SlotId.Hex, StringComparer.Ordinal).ToImmutableArray();
    }
    internal static bool Prefix(TypedPath root, TypedPath leaf) => root.Segments.Length <= leaf.Segments.Length && root.Segments.SequenceEqual(leaf.Segments.Take(root.Segments.Length));
}
