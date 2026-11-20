using System.Globalization;
using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

internal sealed partial class WorldStorageUseIndex
{
    private readonly Dictionary<string, HashSet<Rune>?> fontGlyphs = new(StringComparer.Ordinal);
    private bool opaqueDefaultFont;

    private void ObserveFont(string id, HashSet<Rune>? glyphs)
    {
        if (id == "minecraft:default")
        {
            opaqueDefaultFont |= glyphs is null || glyphs.Any(glyph => Rune.GetUnicodeCategory(glyph) != UnicodeCategory.PrivateUse);
            return;
        }
        if (!fontGlyphs.TryGetValue(id, out var earlier))
        {
            Charge(checked(id.Length + (glyphs?.Count ?? 0) * 16));
            fontGlyphs.Add(id, glyphs);
        }
        else if (earlier is null || glyphs is null) fontGlyphs[id] = null;
        else earlier.IntersectWith(glyphs);
    }

    private bool CanPreserveGlyphs(StorageGlyphLiteral literal)
    {
        if (fontGlyphs.GetValueOrDefault(literal.Font) is not { } glyphs) return false;
        foreach (var scalar in literal.Text.EnumerateRunes())
            if (!glyphs.Contains(scalar)) return false;
        return true;
    }

    private static bool HasUnprovenNativeFont(NbtValue value) => value switch
    {
        NbtCompound compound => compound.Entries.Any(entry => entry.Name == "font"
            ? entry.Value is not NbtString { Value: "minecraft:default" or "default" } : HasUnprovenNativeFont(entry.Value)),
        NbtList list => list.Items.Any(HasUnprovenNativeFont),
        _ => false
    };

    private void PropagateAssignmentShapes()
    {
        var pending = new Queue<ListUses>(uses.Values.SelectMany(storage => storage.Paths.Values)
            .Where(value => value.Assignments.Count != 0 && (value.RequiresScalar || value.RequiresList)));
        while (pending.TryDequeue(out var value))
        {
            foreach (var peer in value.Assignments)
            {
                var scalar = value.RequiresScalar || peer.RequiresScalar;
                var list = value.RequiresList || peer.RequiresList;
                if (scalar == peer.RequiresScalar && list == peer.RequiresList) continue;
                peer.RequiresScalar = scalar;
                peer.RequiresList = list;
                pending.Enqueue(peer);
            }
        }
    }

    private sealed class StorageUses
    {
        internal Dictionary<string, ListUses> Paths { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> OtherPaths { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> EquipmentRoots { get; } = new(StringComparer.Ordinal);
        internal bool Ambiguous;
    }

    private sealed class ListUses(string storage, string path)
    {
        internal string Storage { get; } = storage;
        internal string Path { get; } = path;
        internal bool Display;
        internal bool NestedRead;
        internal bool RequiresList;
        internal bool RequiresScalar;
        internal string? EquipmentRoot;
        internal SourceAddress? EquipmentImport;
        internal SourceAddress? LoreReplacement;
        internal SourceAddress? EquipmentExport;
        internal bool RepeatedEquipmentStep;
        internal bool Copies => Incoming.Count != 0 || Outgoing.Count != 0 || EquipmentRoot is not null;
        internal HashSet<NbtType> ElementWrites { get; } = [];
        internal HashSet<NbtType> ReplacementTypes { get; } = [];
        internal HashSet<string> Producers { get; } = new(StringComparer.Ordinal);
        internal HashSet<ListUses> Incoming { get; } = [];
        internal HashSet<ListUses> Outgoing { get; } = [];
        internal HashSet<ListUses> Assignments { get; } = [];
        internal HashSet<StorageGlyphLiteral>? GlyphLiterals;
    }

    private sealed record ListCandidate(ListUses List, ArtifactAddress? Artifact = null, StorageTextRule? Rule = null);
}
