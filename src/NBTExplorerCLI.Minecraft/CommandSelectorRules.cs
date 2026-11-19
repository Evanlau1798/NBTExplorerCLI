using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private readonly Dictionary<int, string?> selectorTypes = [];
    private void Selectors(List<CommandToken> tokens, TypedPath path, Func<TypedPath, int, int, string, SourceAddress>? locate = null)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Text.Length < 2 || token.Text[0] != '@') continue;
            var parsed = SelectorNames.Parse(token.Text, limits, MinecraftProfiles.SupportsNearestSelector(profile));
            selectorTypes[token.Start] = parsed.EntityType;
            TypedPath Member(int member) => member < 0 ? path : At(At(At(path, "selector"), index), member);
            foreach (var name in parsed.Names)
            {
                var node = Member(name.Member); var start = token.Start + name.Start;
                var address = locate?.Invoke(node, start, name.Length, "reference.selector.name") ?? Address(node, start, name.Length, "reference.selector.name");
                Charge(address, name.Name); references.Add(new(address, name.Name, name.Negated));
                if (selectorReference is not null)
                {
                    Charge(address, token.Text);
                    selectorReference(new(new(address, name.Name, name.Negated), token.Text, parsed.Kind, parsed.EntityType, locate is null));
                }
            }
            foreach (var diagnostic in parsed.Diagnostics)
            {
                var node = Member(diagnostic.Member); var start = token.Start + diagnostic.Start;
                if (locate is null) Unknown(node, start, diagnostic.Length, diagnostic.Code);
                else
                {
                    var address = locate(node, start, diagnostic.Length, source.Address.SemanticRole);
                    Charge(address, diagnostic.Code); diagnostics.Add(new(address, diagnostic.Code)); completeness = ReferenceCompleteness.Unresolved;
                }
            }
        }
    }
}
