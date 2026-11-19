using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

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
        var early = MinecraftProfiles.ForDataVersion(4298) ?? throw new InvalidOperationException("25w02a needs an NBT text profile");
        var authoring = new SourceAddress(new(World, "snbt-transition.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var homogeneous = CommandTextExtractor.Extract(new(authoring, "tellraw @s [{text:'One'},{text:'Two'}]"), early);
        Require(homogeneous.Complete && homogeneous.Text.Occurrences.Select(row => row.Text).SequenceEqual(new[] { "One", "Two" }),
            "early native NBT components use the original homogeneous SNBT lists");
        var mixed = new CommandCandidate(authoring, "tellraw @s ['One',{text:'Two'}]");
        Require(!CommandTextExtractor.Extract(mixed, early).Complete
            && CommandTextExtractor.Extract(mixed, MinecraftProfiles.ForDataVersion(4317)!).Complete,
            "native text began before the grammar allowed heterogeneous lists");
        const string hover = "tellraw @s {text:'Open',hover_event:{action:'show_text',text:{text:'Hint',extra:[{selector:'@e[name=Guard]'}]}}}";
        var hovered = CommandTextExtractor.Extract(new(authoring, hover), early);
        Require(hovered.Complete && hovered.Text.Occurrences.Any(row => row.Text == "Hint") && hovered.References.Single().Name == "Guard",
            "the first inline hover field contributes both text and selector references");
        var stored = new NbtCompound([new("text", new NbtString("Open")), new("extra", new NbtList(NbtType.Compound,
            [new NbtCompound([new("", new NbtString("Wrapped"))]), new NbtCompound([new("text", new NbtString("Styled"))])]))]);
        var storedAddress = new SourceAddress(new(World, "scoreboard.dat", ArtifactKind.Nbt), TypedPath.Root, "scoreboard.display");
        var storedText = TextComponentExtractor.Nbt(storedAddress, stored, early);
        Require(storedText.Complete && storedText.Occurrences.Any(row => row.Text == "Wrapped"),
            "native binary text already used wrappers before command SNBT allowed mixed lists");
        var clickPayload = System.Text.Json.JsonSerializer.Serialize("/tellraw @s {text:'\\u0041'}");
        var clicked = new CommandCandidate(authoring, "tellraw @s {text:'Open',click_event:{action:'run_command',command:" + clickPayload + "}}");
        Require(!CommandTextExtractor.Extract(clicked, early).Complete
            && CommandTextExtractor.Extract(clicked, MinecraftProfiles.ForDataVersion(4317)!).Text.Occurrences.Any(row => row.Text == "A"),
            "nested click commands use the same SNBT grammar as their enclosing profile");
        var mixedLists = MinecraftProfiles.ForDataVersion(4308)!;
        Require(CommandTextExtractor.Extract(mixed, mixedLists).Complete && !CommandTextExtractor.Extract(clicked, mixedLists).Complete,
            "mixed lists arrived before extended escapes and numbers");
        var grammar = MinecraftProfiles.SnbtGrammar(mixedLists);
        var list = Snbt.ParseDocument("['One',{text:'Two'}]"u8.ToArray(), grammar).Root.Value;
        Require(Snbt.ParseDocument(Snbt.Write(list, grammar), grammar).Root.Value == list, "the transitional SNBT writer retains mixed list element types");
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

    public static void StaticScoreControlPreservesNestedTextAndSelectors()
    {
        foreach (var version in new[] { 3953, 4774 })
        {
            const string commands = "execute if score @e[type=sheep,name=Rainbow,limit=1] mode matches ..2 run bossbar set golden:mode name {\"text\":\"Pink mode\"}\n"
                + "execute unless score @s mode = #expected mode run scoreboard players add @s mode 1\n"
                + "execute store result score @s mode run time query gametime\n"
                + "title @a times 10 70 20\nscoreboard objectives add mode dummy\nscoreboard objectives setdisplay sidebar mode\n"
                + "scoreboard players operation @s mode %= #modulus mode\nbossbar set golden:mode color purple\n"
                + "bossbar set golden:mode max 100\nbossbar set golden:mode value 75\nbossbar set golden:mode visible true\n"
                + "bossbar set golden:mode players @a\nbossbar set golden:mode style notched_10\nkill @e[tag=arena_preview]\n"
                + "loot give @s loot golden:book\nfunction golden:titles/update\n";
            var source = new SourceAddress(new(World, "score-actions.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            var extracted = CommandTextExtractor.Extract(new(source, commands), profile);
            Require(extracted.Complete && extracted.Text.Occurrences.Single().Text == "Pink mode" && extracted.References.Single().Name == "Rainbow",
                "static scoreboard control exposes the nested bossbar text and selector without classifying the source as unknown");
            var syntax = extracted.Text.Occurrences[0].Address.Syntax!;
            Require(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(commands).AsSpan((int)syntax.StartByte, (int)syntax.LengthBytes)) == "\"Pink mode\"",
                "nested text remains editable through its exact original byte span");
            foreach (var unknown in new[] { "execute if score @s mode matches .. run tellraw @s \"text\"", "execute if score @s mode not_a_comparator @s mode run tellraw @s \"text\"",
                "execute store result storage golden:data field int 1 run time query gametime", "bossbar set golden:mode color rainbow", "function golden:titles/update with storage golden:data", "loot give @s fish golden:book" })
            {
                var rejected = CommandTextExtractor.Extract(new(source, unknown), profile);
                Require(!rejected.Complete && rejected.Text.Occurrences.IsEmpty, "unrecognized or dynamic shapes still fail closed: " + unknown);
            }
        }
    }

    public static void StaticClickActionsDoNotClaimDynamicNameReferences()
    {
        var command = new SourceAddress(new(World, "clicks.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var json = new SourceAddress(new(World, "clicks.json", ArtifactKind.Json), TypedPath.Root, "book.page");
        var nbt = new SourceAddress(new(World, "clicks.dat", ArtifactKind.Nbt), TypedPath.Root, "book.page");
        foreach (var version in new[] { 3953, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            var clickField = version == 3953 ? "clickEvent" : "click_event";
            foreach (var (action, body, expected) in new[] {
                ("open_url", version == 3953 ? "\"value\":\"https://example.com\"" : "\"url\":\"https://example.com\"", true),
                ("change_page", version == 3953 ? "\"value\":\"1\"" : "\"page\":1", true),
                ("copy_to_clipboard", "\"value\":\"@e[name=Guard]\"", true),
                ("run_command", version == 3953 ? "\"value\":\"/kill @e[name=Guard]\"" : "\"command\":\"/kill @e[name=Guard]\"", false),
                ("unknown_action", "\"value\":\"unused\"", false)
            })
            {
                var component = "{\"text\":\"Open\",\"" + clickField + "\":{\"action\":\"" + action + "\"," + body + "}}";
                var parsed = CommandTextExtractor.Extract(new(command, "tellraw @s " + component), profile);
                Require(parsed.Complete == expected && parsed.Text.Occurrences.Single().Text == "Open" && parsed.References.IsEmpty,
                    "a known non-command click action has no name reference; command and unknown clicks remain unresolved");
                Require((NativeComponentReferences.Json(json, Encoding.UTF8.GetBytes(component), profile).Completeness == ReferenceCompleteness.Complete) == expected,
                    "standalone JSON names and command components agree on supported static clicks");
                var page = version == 3953 ? "'" + component + "'" : component;
                var book = CommandTextExtractor.Extract(new(command, "give @s written_book[written_book_content={title:'Title',author:'Author',pages:[" + page + "]}]"), profile);
                Require(book.Complete == expected && book.Text.Occurrences.Any(occurrence => occurrence.Text == "Open"),
                    "nested book click actions follow the same static policy");
                if (version == 4774)
                {
                    var modern = new NbtCompound([new("text", new NbtString("Open")), new("click_event", new NbtCompound([new("action", new NbtString(action))]))]);
                    Require((NativeComponentReferences.Nbt(nbt, modern, profile).Completeness == ReferenceCompleteness.Complete) == expected,
                        "native NBT uses the same click action reference policy as command and JSON components");
                }
            }
        }
    }

    public static void FunctionTagsAndClickPayloads()
    {
        var source = new SourceAddress(new(World, "click-payloads.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        foreach (var version in new[] { 3953, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            string Click(string payload) => "tellraw @s {\"text\":\"Open\","
                + (version == 3953 ? "\"clickEvent\":{\"action\":\"run_command\",\"value\":" : "\"click_event\":{\"action\":\"run_command\",\"command\":")
                + System.Text.Json.JsonSerializer.Serialize(payload) + "}}";
            CommandExtractionResult Extract(string command) => CommandTextExtractor.Extract(new(source, command), profile);
            foreach (var (command, complete) in new[] {
                ("function #golden:load", true), ("function golden:load", true), ("function #", false),
                ("function ##golden:load", false), ("function #golden:load with storage golden:data", false)
            })
            {
                Require(Extract(command).Complete == complete && Extract(Click("/" + command)).Complete == complete, "function resource/tag and click coverage: " + command);
            }
            foreach (var (payload, complete, message) in new[] {
                ("/tp @s ~ ~10 ~", true, (string?)null), ("/tp @e[name=Guard] ~ ~10 ~", false, null),
                ("/kick @s Goodbye", true, "Goodbye"), ("/kick @s Hello @e[name=Guard]", false, null)
            })
            {
                var result = Extract(Click(payload));
                Require(result.Complete == complete && result.Text.Occurrences.SingleOrDefault(row => row.Context == "command.click.message")?.Text == message,
                    "only proven click commands extract their editable message: " + payload);
            }
            Require(!Extract("loot give @s loot #golden:load").Complete, "only the function argument accepts a tag");
        }
    }

    public static void StaticMapControlsKeepExactSelectorNames()
    {
        var source = new SourceAddress(new(World, "region/r.0.0.mca", ArtifactKind.Region), TypedPath.Root, "command.block", "minecraft:overworld",
            new ChunkPosition(0, 0));
        var profile = MinecraftProfiles.ForDataVersion(4671)!;
        foreach (var command in new[] {
            "/tp @e[type=minecraft:fox,name=Rocky] -71.45 74.00 -113.17",
            "/tag @e[type=minecraft:fox,name=Rocky] add checkpointOne",
            "/execute as @e[type=minecraft:fox,name=Rocky] run effect give @s minecraft:darkness 15 1 true",
            "/playsound minecraft:block.anvil.land master @e[type=minecraft:fox,name=Rocky] ~ ~ ~ 1 0.1"
        })
        {
            var result = CommandTextExtractor.Extract(new(source, command), profile);
            Require(result.Complete && result.Text.Occurrences.IsEmpty && result.References.Single().Name == "Rocky",
                "nontext controls still index their real typed selector: " + command);
        }
        foreach (var (command, expected) in new[] {
            ("/fill 84 59 26 84 61 28 air destroy", true), ("/fill 84 59 26 84 61 28 oak_sign{front_text:{}}", false),
            ("/tp @s ~ nope ~", false), ("/effect give @p minecraft:darkness 15 1 maybe", false),
            ("/playsound minecraft:block.anvil.land master @p ~ bad ~", false), ("/tag @s add", false),
            ("/tag @ add checkpointOne", false), ("/tp * ~ ~ ~", false), ("/effect clear @", false),
            ("/playsound minecraft:block.anvil.land master *", false), ("/tag Evan_lau add checkpointOne", true),
            ("/tag 15e6d9d5-567f-44df-94c4-22928f73a935 list", true), ("/tag @z add checkpointOne", false),
            ("/scoreboard players set @ objective 1", false), ("/scoreboard players get * objective", false),
            ("/scoreboard players set * objective 1", true), ("/scoreboard players get #hidden objective", true),
            ("/scoreboard players operation * left += * right", true)
        })
            Require(CommandTextExtractor.Extract(new(source, command), profile).Complete == expected, "static control grammar boundary: " + command);
    }

    public static void StaticParticleDoesNotBlockAdjacentTranslation()
    {
        var source = new SourceAddress(new(World, "particles.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(4671)!;
        var input = "tellraw @a {text:'The aura has dissipated'}\nexecute at @a run particle flame ~ ~ ~ 0.5 1 0.5 0.05 150 normal\n";
        var parsed = CommandTextExtractor.Extract(new(source, input), profile);
        Require(parsed.Complete && parsed.Text.Occurrences.Single().Text == "The aura has dissipated", "a particle without a structured payload does not block the function's literal");
        foreach (var command in new[] { "particle minecraft:item{item:{id:'minecraft:paper'}} ~ ~ ~ 0 0 0 0 1 normal", "particle flame ~ ~ ~ 0 0 0 0 -1 normal" })
            Require(!CommandTextExtractor.Extract(new(source, command), profile).Complete, "unknown particle payload or invalid particle arguments remain unresolved");
        var java1201 = MinecraftProfiles.ForDataVersion(3465)!;
        foreach (var command in new[] {
            "scoreboard players set $timer countdown 4",
            "execute if score $timer countdown matches 1.. run function p:advance",
            "execute store result score $count counts if entity @e[nbt={HurtTime:10s}]",
            "execute store result score $pass counts if score $count counts matches 27..",
            "execute store result score $pass counts unless block ~0.8 ~ ~ #example:solid",
            "particle block minecraft:sculk ~ ~ ~ 0.3 0.5 0.3 0.01 2 normal",
            "particle dust 0.3 0.4 0 1 ~ ~ ~ 0 0 0 0 1 force",
            "particle dust_color_transition 0.3 0.4 0 1 0.9 0.1 0.2 ~ ~ ~ 0 0 0 0 1 force",
            "particle minecraft:squid_ink ~ ~ ~ 0 0 0 0 1 normal",
            "data modify entity @s CustomNameVisible set value 0b"
        })
        {
            var result = CommandTextExtractor.Extract(new(source, command + "\ntellraw @s {\"text\":\"Guide\"}"), java1201);
            Require(result.Complete && result.Text.Occurrences.Single().Text == "Guide", "valid nontext operands preserve neighboring translations: " + command);
        }
        foreach (var command in new[] {
            "scoreboard players set $ countdown 4", "particle block oak_sign{CustomName:'Guide'} ~ ~ ~ 0 0 0 0 1 normal",
            "execute store result score $count counts", "execute positioned ~ ~ ~",
            "execute store result score $pass counts if block 0.8 0 0 stone",
            "particle dust ~ ~ ~ 0 0 0 0 1 force",
            "particle dust_color_transition 0.3 0.4 0 1 0.9 0.1 ~ ~ ~ 0 0 0 0 1 force",
            "data modify entity @s CustomNameVisible set value 'Guide'"
        })
            Require(!CommandTextExtractor.Extract(new(source, command), java1201).Complete, "dynamic or invalid operands remain unresolved: " + command);
        foreach (var command in new[] {
            "execute if block 353 125 -115 minecraft:honey_block run tellraw @a {text:'Test'}",
            "execute if items block 132 60 -31 contents minecraft:music_disc_wait run tellraw @a {text:'Test'}",
            "execute if items entity @s weapon.offhand #minecraft:shulker_boxes run tellraw @a {text:'Test'}"
        })
        {
            var conditional = CommandTextExtractor.Extract(new(source, command), profile);
            Require(conditional.Complete && conditional.Text.Occurrences.Single().Text == "Test", "simple block/item predicates preserve the exact nested command");
        }
        Require(!CommandTextExtractor.Extract(new(source, "execute if items entity @s weapon.offhand paper[custom_name='Guard'] run function golden:load"), profile).Complete,
            "component predicate remains unresolved");
        var staticallyStored = CommandTextExtractor.Extract(new(source,
            """setblock 313 144 -90 repeating_command_block[conditional=false]{Command:"/execute as @a at @p run effect give @p minecraft:wither 15 3"}"""), profile);
        Require(staticallyStored.Complete && staticallyStored.Text.Occurrences.IsEmpty, "a verified literal-free stored command needs no nested rewrite");
        foreach (var command in new[] {
            """setblock 313 144 -90 repeating_command_block{Command:"/tellraw @s {text:'Visible'}"}""",
            """setblock 313 144 -90 repeating_command_block{Command:"/kill @e[type=fox,name=Rocky]"}"""
        })
            Require(!CommandTextExtractor.Extract(new(source, command), profile).Complete, "stored commands with text or named references stay unresolved");
        var macro = CommandTextExtractor.Extract(new(source,
            "$item replace entity @s $(slot) with white_shulker_box[container=[\\\r\n{slot:1,item:{id:'minecraft:paper'}}\\\r\n]]\r\ntellraw @s {text:'After the macro'}"), profile);
        Require(!macro.Complete && macro.Text.Diagnostics is [{ Code: "DynamicCommandMacro" }]
            && macro.Text.Occurrences.Single().Text == "After the macro", "multiline macro stays unresolved while the following command keeps its address");
    }

    public static void SelfScoreComponentsKeepAdjacentTextAndProtectOtherSelectors()
    {
        var command = new SourceAddress(new(World, "self-score.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var json = new SourceAddress(new(World, "self-score.json", ArtifactKind.Json), TypedPath.Root, "book.page");
        var nbt = new SourceAddress(new(World, "self-score.dat", ArtifactKind.Nbt), TypedPath.Root, "book.page");
        foreach (var version in new[] { 3953, 4774 })
        {
            var profile = MinecraftProfiles.ForDataVersion(version)!;
            foreach (var name in new[] { "@s", "@e[type=sheep,name=Guard,limit=1]", "@s[name=Guard]" })
            {
                var isSelf = name == "@s";
                var score = "{\"score\":{\"name\":\"" + name + "\",\"objective\":\"temp_display\"}}";
                var component = "[{\"text\":\"Current: \"}," + score + "]";
                var text = CommandTextExtractor.Extract(new(command, "tellraw @s " + component), profile);
                Require(text.Complete == isSelf && text.Text.Occurrences.Single().Text == "Current: " && text.References.IsEmpty,
                    "plain self scores cannot use an entity name; a score with a selector predicate stays unresolved");
                Require((NativeComponentReferences.Json(json, Encoding.UTF8.GetBytes(component), profile).Completeness == ReferenceCompleteness.Complete) == isSelf,
                    "native JSON does not confuse the executor's score with a dynamic entity-name reference");
                var page = version == 3953 ? "'" + component + "'" : component;
                var book = CommandTextExtractor.Extract(new(command, "give @s written_book[written_book_content={title:'Title',author:'Author',pages:[" + page + "]}]"), profile);
                Require(book.Complete == isSelf && book.Text.Occurrences.Any(occurrence => occurrence.Text == "Current: "),
                    "nested item components preserve the same self score reference boundary");
                if (version == 4774)
                {
                    var modern = new NbtCompound([new("score", new NbtCompound([new("name", new NbtString(name)), new("objective", new NbtString("temp_display"))]))]);
                    Require((NativeComponentReferences.Nbt(nbt, modern, profile).Completeness == ReferenceCompleteness.Complete) == isSelf,
                        "native NBT distinguishes a self score from a potentially name-dependent score");
                }
            }
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
    public static void SummonSetblockAndTaggedGive()
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
