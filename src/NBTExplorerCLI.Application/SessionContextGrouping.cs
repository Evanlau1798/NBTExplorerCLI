using System.Collections.Immutable;
using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Application;

internal static class SessionContextGrouping
{
    internal static ImmutableArray<SessionContextGroup> Build(IEnumerable<TextSlot> slots, ImmutableArray<SessionGroupRoot> contextRoots = default)
    {
        var roots = (contextRoots.IsDefault ? [] : contextRoots).ToDictionary(root => root.SlotId, root => root.Container);
        var groups = slots.Select(slot => (Slot: slot, Binding: Binding(slot.Address, roots.GetValueOrDefault(slot.Id.Digest))))
            .GroupBy(item => SourceIdentity.Group(item.Binding.Kind, item.Binding.Container, 1));
        return groups.Select(group =>
        {
            var first = group.First(); var ordered = group.OrderBy(item => OrderKey(item.Slot.Address), StringComparer.Ordinal).Select(item => item.Slot).ToArray();
            return new SessionContextGroup(first.Binding.Kind, first.Binding.Container, group.Key, SourceIdentity.Members(group.Key, ordered.Select(slot => slot.Id), 0), ordered.Select(slot => slot.Id.Digest).ToImmutableArray());
        }).OrderBy(group => group.GroupId.Digest.Hex, StringComparer.Ordinal).ToImmutableArray();
    }
    private static (string Kind, SourceAddress Container) Binding(SourceAddress address, SourceAddress? declared)
    {
        var role = address.SemanticRole;
        while (role.EndsWith(".fallback", StringComparison.Ordinal) || role.EndsWith(".hover", StringComparison.Ordinal)) role = role[..role.LastIndexOf('.')];
        var kind = role is "book.page" or "lectern.book.page" ? "book.pages"
            : role.StartsWith("sign.front", StringComparison.Ordinal) || role.StartsWith("hanging_sign.front", StringComparison.Ordinal) ? "sign.front"
            : role.StartsWith("sign.back", StringComparison.Ordinal) || role.StartsWith("hanging_sign.back", StringComparison.Ordinal) ? "sign.back"
            : role == "item.lore" ? "item.lore" : "styled.fragments";
        if (declared is not null) return (kind, new(declared.Artifact, declared.NodePath, "session.group." + kind, declared.Dimension, declared.Chunk));
        if (role is "command_storage.component" or "command_storage.component.interpreted")
            throw new InvalidOperationException("Storage component grouping requires a bound scan root; rescan and start again");
        var path = address.Syntax is null ? ContainerPath(address.NodePath, address.ComponentPath is null, role) : address.NodePath;
        var syntax = address.Syntax;
        if (syntax?.NodePath is { } syntaxPath)
        {
            var command = syntax.GrammarProfile.StartsWith("java-commands-", StringComparison.Ordinal);
            var container = syntax.GrammarProfile.StartsWith("java-snbt-", StringComparison.Ordinal) || command && !role.StartsWith("command.", StringComparison.Ordinal)
                ? ContainerPath(syntaxPath, address.ComponentPath is null, role)
                : command && syntaxPath.Segments.Length > 0 && syntaxPath.Segments[0].Kind == PathSegmentKind.Index
                    ? new TypedPath(syntaxPath.Segments.Take(Math.Min(2, syntaxPath.Segments.Length))) : syntaxPath;
            syntax = new(syntax.GrammarProfile, syntax.GrammarVersion, 0, 0, syntax.PreimageHash, syntax.Encoding, syntax.Newline, container);
        }
        return (kind, new(address.Artifact, path, "session.group." + kind, address.Dimension, address.Chunk, syntax: syntax));
    }
    private static TypedPath ContainerPath(TypedPath path, bool nativeComponent, string role)
    {
        if (role is "language.value" or "command_storage.literal") return path;
        var marker = -1;
        for (var index = 0; index < path.Segments.Length; index++)
        {
            if (role.StartsWith("sign.front", StringComparison.Ordinal) && path.Segments[index].Key is "Text1" or "Text2" or "Text3" or "Text4")
                return new(path.Segments.Take(index));
            if (path.Segments[index].Kind == PathSegmentKind.Key && path.Segments[index].Key is "pages" or "filtered_pages" or "messages" or "filtered_messages" or "Lore" or "lore" or "minecraft:lore") marker = index;
        }
        if (marker >= 0) return new(path.Segments.Take(marker + 1));
        if (nativeComponent)
        {
            // Schema component roots precede their array fragments; entity/item list indices belong to the parent.
            for (var index = 0; index < path.Segments.Length; index++)
                if (path.Segments[index].Kind == PathSegmentKind.Key && IsComponentRoot(path.Segments[index].Key!, role)) return new(path.Segments.Take(index + 1));
            for (var index = 0; index < path.Segments.Length; index++)
                if (path.Segments[index].Kind == PathSegmentKind.Key && path.Segments[index].Key is "text" or "extra" or "with" or "fallback") return new(path.Segments.Take(index));
        }
        return path;
    }
    private static bool IsComponentRoot(string key, string role) => role switch
    {
        "text_display.text" => key == "text",
        "item.name" => key is "Name" or "name" or "custom_name" or "minecraft:custom_name" or "item_name" or "minecraft:item_name",
        "entity.name" or "armor_stand.name" or "container.name" or "command_block.name" => key == "CustomName",
        "bossbar.name" => key == "Name",
        "scoreboard.objective.name" or "scoreboard.team.name" => key == "DisplayName",
        "scoreboard.team.prefix" => key == "MemberNamePrefix",
        "scoreboard.team.suffix" => key == "MemberNameSuffix",
        "advancement.title" => key == "title",
        "advancement.description" or "pack.description" => key == "description",
        _ => false
    };
    private static string OrderKey(SourceAddress address)
    {
        static string Path(TypedPath? path) => path is null ? "" : string.Concat(path.Segments.Select(segment => segment.Kind == PathSegmentKind.Index
            ? "I" + segment.Index!.Value.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + "/"
            : "K" + Rank(segment.Key!) + string.Concat(segment.Key!.Select(unit => ((int)unit).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))) + "/"));
        return Path(address.NodePath) + "|" + Path(address.Syntax?.NodePath) + "|" + Path(address.ComponentPath) + "|" + address.SemanticRole;
    }
    private static string Rank(string key) => key switch { "text" => "0", "raw" => "1", "filtered" => "2", "fallback" => "3", "with" => "4", "extra" => "5", "hoverEvent" or "hover_event" => "6", _ => "7" };
}
