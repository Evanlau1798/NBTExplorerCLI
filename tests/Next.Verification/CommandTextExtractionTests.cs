using System.Text;
using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CommandTextExtractionTests
{
    public static void ExecuteJsonComponentsAndReferences()
    {
        const string script = "# ignored\r\nexecute as @a[name=\"North Guard\"] at @s run tellraw @s [{\"text\":\"Same\"},{\"translate\":\"test.key\",\"with\":[\"Same\"]}]\r\ntitle @a subtitle \"Scalar雪\"\r\n";
        var bytes = Encoding.UTF8.GetBytes(script);
        var source = new SourceAddress(new(World, "datapacks/p/data/test/function/start.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function",
            syntax: new("java-commands-1.21", 1, 0, bytes.Length, Hash256.Compute(bytes).Hex, "utf8", "crlf"));
        var result = CommandTextExtractor.Extract(new(source, script), MinecraftProfiles.ForDataVersion(3953)!);
        Require(result.Complete && result.Text.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Same", "Same", "Scalar雪" })
            && result.Text.TranslationKeys.Single().Key == "test.key", "execute-run and scalar/array/compound components expose exact literal leaves and separate key references");
        var tokenStarts = new[] { script.IndexOf("\"Same\"", StringComparison.Ordinal), script.LastIndexOf("\"Same\"", StringComparison.Ordinal), script.IndexOf("\"Scalar雪\"", StringComparison.Ordinal) };
        foreach (var (text, index) in result.Text.Occurrences.Select((text, index) => (text, index)))
        {
            var token = index == 2 ? "\"Scalar雪\"" : "\"Same\""; var locator = text.Address.Syntax!;
            Require(text.Address.Artifact == source.Artifact && text.Address.NodePath == TypedPath.Root && locator.StartByte == Encoding.UTF8.GetByteCount(script[..tokenStarts[index]])
                && locator.LengthBytes == Encoding.UTF8.GetByteCount(token) && locator.PreimageHash == Hash256.Compute(bytes).Hex && locator.Newline == "crlf"
                && locator.NodePath is not null && SourceAddressJson.Read(SourceAddressJson.Write(text.Address)) == text.Address, "command slots retain exact whole-source preimage and token byte spans");
        }
        Require(result.Text.Occurrences.Select(text => SourceIdentity.Occurrence(text.Address)).Distinct().Count() == 3, "same text in different grammar leaves has distinct stable occurrence identities");
        var reference = result.References.Single();
        Require(reference.Name == "North Guard" && !reference.Negated && reference.Address.Syntax!.StartByte == script.IndexOf("\"North Guard\"", StringComparison.Ordinal)
            && reference.Address.Syntax.LengthBytes == Encoding.UTF8.GetByteCount("\"North Guard\""), "selector name is a separate exact quoted-token reference candidate");
    }
    public static void ModernSnbtAndUnknownGrammar()
    {
        foreach (var version in new[] { 4325, 4774 })
        {
            const string command = "execute as @e[name=!\"Guard\"] run tellraw @s ['Same',{text:'Same',hover_event:{action:'show_text',value:{text:'Hover雪'}}}]\r";
            var bytes = Encoding.UTF8.GetBytes(command);
            var binding = new SourceAddress(new(World, "test.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var result = CommandTextExtractor.Extract(new(binding, command), MinecraftProfiles.ForDataVersion(version)!);
            Require(result.Complete && result.Text.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Same", "Same", "Hover雪" })
                && result.References.Single().Negated && result.References.Single().Name == "Guard", "modern scalar/compound/array SNBT components and negated selector references use exact grammar");
            var tokens = new[] { "'Same'", "'Same'", "'Hover雪'" }; var after = 0;
            foreach (var (text, index) in result.Text.Occurrences.Select((text, index) => (text, index)))
            {
                var start = command.IndexOf(tokens[index], after, StringComparison.Ordinal); after = start + tokens[index].Length;
                Require(text.Address.Syntax!.StartByte == Encoding.UTF8.GetByteCount(command[..start]) && text.Address.Syntax.LengthBytes == Encoding.UTF8.GetByteCount(tokens[index])
                    && text.Address.Syntax.PreimageHash == Hash256.Compute(bytes).Hex && text.Address.Syntax.Newline == "cr", "modern syntax spans bind leaf spelling and whole command preimage");
            }
        }
        var source = new SourceAddress(new(World, "test.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        foreach (var body in new[] { "$tellraw @s $(message)", "custom:future @a[name=Guard] {text:'Protected'}", "execute unknown run tellraw @s {\"text\":\"Protected\"}" })
        {
            var unknown = CommandTextExtractor.Extract(new(source, body), MinecraftProfiles.ForDataVersion(3953)!);
            Require(!unknown.Complete && unknown.ReferenceCompleteness == ReferenceCompleteness.Unresolved && unknown.Text.Occurrences.IsEmpty && !unknown.Text.Diagnostics.IsEmpty,
                "unknown grammar and macro never become zero-item complete or a global text guess");
        }
    }
    public static void ItemComponentFragments()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var name = modern ? "{text:'Name'}" : "'{\"text\":\"Name\"}'";
            var lore = modern ? "[{text:'Lore'}]" : "['{\"text\":\"Lore\"}']";
            var script = "give @s minecraft:paper[:custom_name=" + name + ",minecraft:lore=" + lore + "] 2";
            var source = new SourceAddress(new(World, "commands.nbt", ArtifactKind.Nbt), new([PathSegment.ForKey("Command")]), "command.block");
            var result = CommandTextExtractor.Extract(new(source, script), MinecraftProfiles.ForDataVersion(version)!);
            Require(result.Complete && result.Text.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Name", "Lore" }), "give parses multiple exact component assignment SNBT fragments, not first balanced brace only");
            foreach (var text in result.Text.Occurrences)
            {
                var token = "'" + (modern ? text.Text : "{\"text\":\"" + text.Text + "\"}") + "'";
                Require(text.Address.NodePath == source.NodePath && text.Address.ComponentPath?.ToJsonPointer() == (modern ? null : "/text")
                    && text.Address.Syntax!.StartByte == Encoding.UTF8.GetByteCount(script[..script.IndexOf(token, StringComparison.Ordinal)]) && text.Address.Syntax.LengthBytes == token.Length,
                    "physical command-block location, SNBT token span and embedded component path remain independent");
            }
        }
    }
    public static void CommentsPreimagesAndBudgets()
    {
        const string script = "# comment with unmatched [ and \"\r\ntellraw @s \"Name\"\r\n";
        var bytes = Encoding.UTF8.GetBytes(script); byte[] raw = [0xef, 0xbb, 0xbf, .. bytes];
        var source = new SourceAddress(new(World, "bom.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function",
            syntax: new("java-commands-1.21", 1, 3, bytes.Length, Hash256.Compute(raw).Hex, "utf8-bom", "crlf"));
        var result = CommandTextExtractor.Extract(new(source, script), MinecraftProfiles.ForDataVersion(3953)!);
        Require(result.Complete && result.Text.Occurrences.Single().Address.Syntax!.StartByte == 3 + script.IndexOf("\"Name\"", StringComparison.Ordinal), "comment contents are not grammar; BOM remains outside component span and inside preimage");
        BinaryNbtTests.RejectCodec(CodecError.InvalidTag, () => CommandTextExtractor.Extract(new(source, script + " "), MinecraftProfiles.ForDataVersion(3953)!));
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => CommandTextExtractor.Extract(new(new(source.Artifact, TypedPath.Root, "command.function"), "tellraw @s \"" + new string('x', 300) + "\""), MinecraftProfiles.ForDataVersion(3953)!, new(MaxDecodedBytes: 512)));
        var scoped = CommandTextExtractor.Extract(new(new(source.Artifact, TypedPath.Root, "command.function"), "tellraw @a[name=Guard] \"Protected\""), MinecraftProfiles.ForDataVersion(3953)!, scope: new([], [], ["language.value"], []));
        Require(scoped.Complete && scoped.Text.Occurrences.IsEmpty && scoped.References.Single().Name == "Guard", "text scope does not erase separately processed exact name references");
    }
    public static void SummonSetblockAndLegacyGive()
    {
        foreach (var (version, modern) in new[] { (3700, false), (3953, false), (4325, true), (4774, true) })
        {
            var name = modern ? "{text:'Entity'}" : "'{\"text\":\"Entity\"}'";
            var line = modern ? "{text:'Sign'}" : "'{\"text\":\"Sign\"}'";
            var script = "summon armor_stand ~ ~ ~ {CustomName:" + name + "}\nsetblock ~ ~ ~ oak_sign[rotation=0]{front_text:{messages:[" + line + "]}} replace\n";
            if (version == 3700) script += "give @s written_book{title:'Book',author:'Author',pages:['{\"text\":\"Page\"}']} 1\n";
            var source = new SourceAddress(new(World, "summons.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var result = CommandTextExtractor.Extract(new(source, script), MinecraftProfiles.ForDataVersion(version)!);
            var expected = version == 3700 ? new[] { "Entity|armor_stand.name", "Sign|sign.front.line", "Book|book.title", "Author|book.author", "Page|book.page" } : new[] { "Entity|armor_stand.name", "Sign|sign.front.line" };
            Require(result.Complete && result.Text.Occurrences.Select(text => text.Text + "|" + text.Address.SemanticRole).SequenceEqual(expected),
                "command grammar supplies explicit entity/block/item schema to authored NBT without inventing source IDs or coordinates");
            Require(result.Text.Occurrences.All(text => text.Address.NodePath == TypedPath.Root && text.Address.Syntax!.PreimageHash == Hash256.Compute(Encoding.UTF8.GetBytes(script)).Hex), "each structured argument shares exact whole-source preimage and physical artifact");
        }
    }
    public static void ComponentSelectorReferences()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var component = modern ? "{selector:'@e[name=Guard]',separator:{text:'/'},extra:[{selector:'@e[name=!Guard]'}]}"
                : "{\"selector\":\"@e[name=Guard]\",\"separator\":{\"text\":\"/\"},\"extra\":[{\"selector\":\"@e[name=!Guard]\"}]}";
            var source = new SourceAddress(new(World, "selectors.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var result = CommandTextExtractor.Extract(new(source, "tellraw @s " + component), MinecraftProfiles.ForDataVersion(version)!);
            Require(result.Complete && result.Text.Occurrences.Single().Text == "/" && result.References.Select(reference => reference.Name + "|" + reference.Negated).Order(StringComparer.Ordinal).SequenceEqual(new[] { "Guard|False", "Guard|True" }),
                "protected component selectors expose separate name references, including nested extra; separator remains literal");
            Require(result.References.Select(reference => SourceIdentity.Occurrence(reference.Address)).Distinct().Count() == 2 && result.References.All(reference => reference.Address.Syntax!.PreimageHash == Hash256.Compute(Encoding.UTF8.GetBytes("tellraw @s " + component)).Hex),
                "component references retain distinct stable grammar leaves and whole-source preimage");
            var dynamic = CommandTextExtractor.Extract(new(source, modern ? "tellraw @s {nbt:'CustomName',entity:'@s',interpret:true}" : "tellraw @s {\"nbt\":\"CustomName\",\"entity\":\"@s\",\"interpret\":true}"), MinecraftProfiles.ForDataVersion(version)!);
            Require(!dynamic.Complete && dynamic.ReferenceCompleteness == ReferenceCompleteness.Unresolved, "dynamic text data cannot prove absence of name references");
        }
    }
    public static void ConditionalDataAndItemCommands()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var entity = modern ? "{text:'Entity'}" : "'{\"text\":\"Entity\"}'";
            var item = modern ? "{text:'Item'}" : "'{\"text\":\"Item\"}'";
            var text = modern ? "{text:'Conditional'}" : "{\"text\":\"Conditional\"}";
            var script = "execute if entity @e[name=Guard] run tellraw @s " + text + "\n"
                + "data merge entity @e[type=armor_stand,limit=1] {CustomName:" + entity + "}\n"
                + "item replace entity @s weapon.mainhand with paper[custom_name=" + item + "]\n";
            var source = new SourceAddress(new(World, "data.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var result = CommandTextExtractor.Extract(new(source, script), MinecraftProfiles.ForDataVersion(version)!);
            Require(result.Complete && result.Text.Occurrences.Select(value => value.Text + "|" + value.Address.SemanticRole).SequenceEqual(new[] {
                "Conditional|command.function.text", "Entity|armor_stand.name", "Item|item.name" }) && result.References.Single().Name == "Guard",
                "execute conditional grammar, exact entity target schema and item replace with route through known argument parsers");
            var unknown = CommandTextExtractor.Extract(new(source, "data merge storage test:arbitrary {value:'Protected'}"), MinecraftProfiles.ForDataVersion(version)!);
            Require(!unknown.Complete && unknown.Text.Occurrences.IsEmpty && unknown.Text.Diagnostics.Single().Code == "UnknownDataTextSchema", "arbitrary storage data does not inherit text schema from a field name");
        }
    }
    public static void GrammarBindingsAndEncodedReferences()
    {
        const string body = "tellraw @a[name=\"雪\\\"Guard\"] \"Text\"";
        var artifact = new ArtifactAddress(World, "bindings.mcfunction", ArtifactKind.McFunction);
        var wrong = new SourceAddress(artifact, TypedPath.Root, "command.function", syntax: new("java-commands-1.21.5", 1, 0, Encoding.UTF8.GetByteCount(body), Hash256.Compute(Encoding.UTF8.GetBytes(body)).Hex, "utf8", "none"));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => CommandTextExtractor.Extract(new(wrong, body), MinecraftProfiles.ForDataVersion(3953)!));
        var correct = new SourceAddress(artifact, TypedPath.Root, "command.function");
        var result = CommandTextExtractor.Extract(new(correct, body), MinecraftProfiles.ForDataVersion(3953)!);
        Require(result.Complete && result.References.Single().Name == "雪\"Guard" && result.References.Single().Address.Syntax!.LengthBytes == Encoding.UTF8.GetByteCount("\"雪\\\"Guard\""),
            "Brigadier escaped quote and nonASCII selector name preserve exact bytes independently from decoded name");
        var duplicate = CommandTextExtractor.Extract(new(correct, "tellraw @a[name=Guard,name=!Other] \"Text\""), MinecraftProfiles.ForDataVersion(3953)!);
        Require(!duplicate.Complete && duplicate.ReferenceCompleteness == ReferenceCompleteness.Unresolved, "multiple name predicates are not silently promoted to a proven complete reference set");
        var invalidEncoding = new SourceAddress(artifact, TypedPath.Root, "command.function", syntax: new("java-commands-1.21", 1, 0, Encoding.UTF8.GetByteCount(body), Hash256.Compute(Encoding.UTF8.GetBytes(body)).Hex, "utf16", "none"));
        BinaryNbtTests.RejectCodec(CodecError.UnsupportedProfile, () => CommandTextExtractor.Extract(new(invalidEncoding, body), MinecraftProfiles.ForDataVersion(3953)!));
    }
    public static void CompleteArgumentBoundaries()
    {
        var function = new SourceAddress(new(World, "boundaries.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        foreach (var body in new[] { "summon armor_stand ~ ~ ~ {CustomName:'{\"text\":\"Name\"}'} unexpected", "give @s paper[custom_name='\"Name\"'] 2 unexpected",
            "give @s paper[custom_name='\"Name\"'] -1", "setblock ~ ~ ~ oak_sign{front_text:{messages:['\"Name\"']}} unexpected" })
        {
            var result = CommandTextExtractor.Extract(new(function, body), MinecraftProfiles.ForDataVersion(3953)!);
            Require(!result.Complete && !result.Text.Diagnostics.IsEmpty, "full argument bounds reject trailing or invalid count/mode instead of silently accepting a prefix: " + body);
        }
        var block = new SourceAddress(new(World, "command.dat", ArtifactKind.Nbt), new([PathSegment.ForKey("Command")]), "command.block");
        var command = CommandTextExtractor.Extract(new(block, "/tellraw @s \"Name\""), MinecraftProfiles.ForDataVersion(3953)!);
        Require(command.Complete && command.Text.Occurrences.Single().Text == "Name" && command.Text.Occurrences.Single().Address.Syntax!.StartByte == 12, "physical command block accepts the game's optional initial slash without moving the original token span");
        Require(!CommandTextExtractor.Extract(new(function, "/tellraw @s \"Name\""), MinecraftProfiles.ForDataVersion(3953)!).Complete, "function syntax does not silently inherit command-block slash handling");
    }
    public static void ItemComponentReferenceCoverage()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var selector = modern ? "{selector:'@e[name=Guard]'}" : "'{\"selector\":\"@e[name=Guard]\"}'";
            var source = new SourceAddress(new(World, "items.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var body = "give @s paper[custom_name=" + selector + "]";
            var result = CommandTextExtractor.Extract(new(source, body), MinecraftProfiles.ForDataVersion(version)!);
            Require(result.Complete && result.Text.Occurrences.IsEmpty && result.References.Length == 1 && result.References.Single().Name == "Guard", "known item name component selector participates in the same reference analysis");
            var book = "give @s written_book[written_book_content={title:'Title',author:'Author',pages:[" + selector + "]}]";
            var nested = CommandTextExtractor.Extract(new(source, book), MinecraftProfiles.ForDataVersion(version)!);
            Require(nested.References.Length == 1 || !nested.Complete && nested.ReferenceCompleteness == ReferenceCompleteness.Unresolved,
                "nested item/book references must be located or explicitly unresolved, never empty complete");
            var name = modern ? "{selector:'@e[name=Guard]'}" : "'{\"selector\":\"@e[name=Guard]\"}'";
            var summon = CommandTextExtractor.Extract(new(source, "summon armor_stand ~ ~ ~ {CustomName:" + name + "}"), MinecraftProfiles.ForDataVersion(version)!);
            Require(summon.References.Length == 1 || !summon.Complete && summon.ReferenceCompleteness == ReferenceCompleteness.Unresolved,
                "references embedded in a structured entity argument cannot become empty complete");
        }
    }
}
