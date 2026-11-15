using Next.Domain;
using static Next.Minecraft.TextExtractionCollector;

namespace Next.Minecraft;

internal sealed partial class NbtTextReader
{
    private void Storage(NbtValue root, NbtValue contents, TypedPath path, IReadOnlyList<StorageTextRule> rules)
    {
        if (rules.Count > 256) throw new ArgumentException("At most 256 explicit storage rules per artifact", nameof(rules));
        var selected = new HashSet<TypedPath>();
        var prefix = path.Segments;
        foreach (var rule in rules)
        {
            if (rule is null || rule.Path is null || !Enum.IsDefined(rule.Kind) || !prefix.SequenceEqual(rule.Path.Segments.Take(prefix.Length))
                || selected.Any(other => IsPrefix(rule.Path, other) || IsPrefix(other, rule.Path)))
                throw new ArgumentException("Storage rules must be distinct nonoverlapping paths within data.contents", nameof(rules));
            selected.Add(rule.Path);
        }
        foreach (var rule in rules)
        {
            var resolved = NbtLocator.Resolve(root, rule.Path);
            if (resolved.Status != ResolutionStatus.Found) { output.Diagnostic(rule.Path, "StorageRuleTargetNotFoundOrAmbiguous"); continue; }
            if (rule.Kind == StorageTextKind.Literal) Literal(resolved.Value, rule.Path, "command_storage.literal");
            else Component(resolved.Value, rule.Path, "command_storage.component");
        }
        Unknown(contents, path);

        void Unknown(NbtValue value, TypedPath current)
        {
            if (selected.Contains(current)) return;
            if (value is NbtString text) output.Unclassified(current, text.Value);
            else if (value is NbtCompound compound)
            {
                if (compound.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != compound.Entries.Length)
                { output.Diagnostic(current, "AmbiguousStorageFields"); return; }
                foreach (var entry in compound.Entries) Unknown(entry.Value, At(current, entry.Name));
            }
            else if (value is NbtList)
                foreach (var (child, childPath) in Sequence(value, current)) Unknown(child, childPath);
        }
    }
    private static bool IsPrefix(TypedPath prefix, TypedPath path)
        => prefix.Segments.Length <= path.Segments.Length && prefix.Segments.SequenceEqual(path.Segments.Take(prefix.Segments.Length));
}
