using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private static readonly string[] ClassicTagTextKeys = ["display", "CustomName", "Text1", "Text2", "Text3", "Text4", "title", "author", "pages", "Command"];

    private void ClassicCommand(string body, int offset, TypedPath path)
    {
        if (profile == MinecraftProfiles.Java125) return;
        var cursor = 0;
        string Word()
        {
            while (cursor < body.Length && body[cursor] == ' ') cursor++;
            var start = cursor;
            while (cursor < body.Length && body[cursor] != ' ') cursor++;
            return body[start..cursor];
        }
        static string Verb(string token) => token.StartsWith('/') ? token[1..] : token;
        var verb = Verb(Word());
        if (MinecraftProfiles.UsesClassicPlainText(profile) && verb is not ("say" or "me" or "tell" or "msg" or "w" or "scoreboard")) return;
        for (var depth = 0; verb == "execute"; depth++)
        {
            if (depth >= limits.MaxDepth) Limit();
            for (var operand = 0; operand < 4; operand++) if (Word().Length == 0) return;
            verb = Verb(Word());
            if (verb == "detect")
            {
                for (var operand = 0; operand < 5; operand++) if (Word().Length == 0) return;
                verb = Verb(Word());
            }
            path = At(path, "execute");
        }
        if (verb is "give" or "summon" or "entitydata" or "setblock")
        {
            var identity = Word();
            if (identity.Length == 0) return;
            if (verb == "give")
            {
                identity = Word();
                for (var index = 0; index < 2; index++) if (Word().Length == 0) return;
            }
            else if (verb == "summon")
                for (var index = 0; index < 3; index++) { if (Word().Length == 0) return; }
            else if (verb == "setblock")
            {
                if (Word().Length == 0 || Word().Length == 0) return;
                identity = Word();
                for (var index = 0; index < 2; index++) if (Word().Length == 0) return;
            }
            while (cursor < body.Length && body[cursor] == ' ') cursor++;
            if (cursor < body.Length && body[cursor] == '{') ClassicTag(verb, identity, body[cursor..], offset + cursor, At(path, verb + ".tag"));
            return;
        }
        var scoreboardGroup = verb == "scoreboard" ? Word() : "";
        var scoreboardName = scoreboardGroup is "objectives" or "teams" && Word() == "add";
        if (scoreboardName && Word().Length == 0) return;
        if (scoreboardName && scoreboardGroup == "objectives" && Word().Length == 0) return;
        if (verb is "tellraw" or "tell" or "msg" or "w" or "title" && Word().Length == 0) return;
        if (verb == "title" && Word() is not ("title" or "subtitle")) return;
        while (cursor < body.Length && body[cursor] == ' ') cursor++;
        var end = body.Length;
        while (end > cursor && body[end - 1] == ' ') end--;
        if (cursor >= end) return;
        var role = source.Address.SemanticRole + ".message";
        if (verb is "tellraw" or "title")
        {
            var component = ClassicTextJson.Normalize(body[cursor..end], limits);
            if (component == "null") return;
            var grammar = At(path, verb + ".text");
            var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, source.Address.SemanticRole + ".text", source.Address.Dimension, source.Address.Chunk);
            var extracted = TextComponentExtractor.NativeJson(binding, NativeJsonStrings.EncodeJavaDocument(component, limits), profile, limits, scope);
            foreach (var row in extracted.Occurrences)
            {
                var address = Address(grammar, offset + cursor, end - cursor, row.Address.SemanticRole, componentPath: row.Address.NodePath);
                Charge(address, row.Text); texts.Add(row with { Address = address });
            }
            foreach (var key in extracted.TranslationKeys)
            {
                var address = Address(grammar, offset + cursor, end - cursor, key.Address.SemanticRole, componentPath: key.Address.NodePath);
                Charge(address, key.Key); keys.Add(key with { Address = address });
            }
            foreach (var error in extracted.Diagnostics) Unknown(grammar, offset + cursor, end - cursor, error.Code);
        }
        else if ((scoreboardName || verb is "say" or "me" or "tell" or "msg" or "w") && WorldSourceScope.IncludesRole(role, scope))
        {
            var message = body[cursor..end];
            var address = Address(At(path, verb + ".message"), offset + cursor, end - cursor, role);
            Charge(address, message);
            texts.Add(new(address, message, role, AllowedEmpty: false));
        }
    }

    private void ClassicTag(string verb, string identity, string tag, int start, TypedPath path)
    {
        // A tag with no supported text fields stays opaque, including pre-existing syntax errors.
        if (!ClassicTagTextKeys.Any(key => tag.Contains(key, StringComparison.Ordinal))) return;
        var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, "command.tag", source.Address.Dimension, source.Address.Chunk);
        var named = MinecraftIdentifier.DefaultNamespace(identity);
        var result = verb switch
        {
            "give" => SnbtTextExtractor.ItemNbtTag(binding, tag, profile, named switch
            { "minecraft:386" => "minecraft:writable_book", "minecraft:387" => "minecraft:written_book", _ => named }, limits, scope),
            "summon" when named.StartsWith("minecraft:", StringComparison.Ordinal) => SnbtTextExtractor.Entity(binding, tag, profile, limits, scope, named),
            "entitydata" => SnbtTextExtractor.Entity(binding, tag, profile, limits, scope, fallbackIdentity: "minecraft:entity"),
            "setblock" => ClassicBlockData(named, binding, tag),
            _ => null
        };
        if (result is not null) MergeComponent(result, path, start, start + tag.Length, true);
    }

    private TextExtractionResult? ClassicBlockData(string block, SourceAddress binding, string tag)
    {
        var tile = block switch
        {
            "minecraft:standing_sign" or "minecraft:wall_sign" => "Sign",
            "minecraft:command_block" => "Control",
            "minecraft:chest" or "minecraft:trapped_chest" => "Chest",
            "minecraft:furnace" or "minecraft:lit_furnace" => "Furnace",
            "minecraft:hopper" => "Hopper",
            "minecraft:dispenser" => "Trap",
            "minecraft:dropper" => "Dropper",
            _ => null
        };
        return tile is null ? null : SnbtTextExtractor.BlockEntity(binding, tag, profile, limits, scope, "minecraft:" + tile);
    }
}
