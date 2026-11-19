using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class CommandReviewTests
{
    private static SourceAddress Source => new(new(World, "review.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
    public static void StaticCoordinateControls()
    {
        StaticResourceAndTeamControls();
        const string commands = """
            execute as @e[name=Guard] run damage @s 2.5 magic
            fill ~-0.5 ~-0.5 ~-0.5 ~0.05 ~0.05 ~0.05 air replace stone
            particle minecraft:snowflake ~ ~ ~ 0 0 0 0.01 4 normal
            tellraw @s {"text":"Visible"}
            """;
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var result = CommandTextExtractor.Extract(new(Source, commands), profile);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Visible" && result.References.Single().Name == "Guard",
            "static damage, fill filters and simple particles protect selectors and leave adjacent text editable");
        foreach (var unsupported in new[]
        {
            "damage @s -2 magic",
            "tp 0 64 1 45 10",
            """fill ~ ~ ~ ~ ~ ~ air replace furnace{CustomName:'{"text":"Guard"}'}""",
            """particle custom:effect{payload:'{"text":"Hidden"}'} ~ ~ ~ 0 0 0 0 1"""
        })
            Require(!CommandTextExtractor.Extract(new(Source, unsupported), profile).Complete,
                "negative damage and structured particle or fill arguments remain unresolved: " + unsupported);
    }

    private static void StaticResourceAndTeamControls()
    {
        const string body = """
            stopsound @a[name=Guard] voice test:alarm
            gamerule doMobSpawning false
            gamerule randomTickSpeed 3
            gamerule keepInventory
            clear @s #test:tools 0
            team join guides @s
            team modify guides color green
            item replace block ~ ~ ~ container.0 from entity @s weapon.mainhand
            data modify storage test:state position set from entity @s Pos[0]
            data get entity @s HandItems[{id:"minecraft:paper"}]
            execute if data entity @s HandItems[{id:"minecraft:paper"}] run scoreboard players get @s proof
            data remove block ~ ~ ~ Items[{id:"minecraft:paper"}]
            tellraw @s {"text":"Guidance"}
            """;
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var result = CommandTextExtractor.Extract(new(Source, body), profile);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Guidance" && result.References.Single().Name == "Guard",
            "static target and resource operands preserve nearby text and vanilla selectors");
        var unsupported = new[]
        {
            "stopsound @a other test:alarm",
            """clear @s paper{display:{Name:'{"text":"Guard"}'}}""",
            "gamerule doMobSpawning maybe", "gamerule doMobSpawning 1", "gamerule randomTickSpeed true", "gamerule invented true",
            "team modify guides color unknown",
            "item replace block ~ ~ ~ bogus from entity @s weapon.mainhand",
            "item replace block ~ ~ ~ container.54 from entity @s weapon.mainhand",
            "item replace block ~ ~ ~ container.0 from entity @s inventory.27",
            "item modify entity @s bogus test:modifier",
            "item replace block ~ ~ ~ container.0 from storage test:unsafe field",
            "data modify storage test:state position set from storage test:unsafe Pos[0]",
            "data modify storage test:state position set from entity @s CustomName",
            "data modify storage test:state copies append from entity @s HandItems[{tag:{display:{Name:'Guard'}}}]",
            """execute if data entity @s HandItems[{tag:{display:{Name:'Guard'}}}] run scoreboard players get @s proof""",
            """data get storage test:state HandItems[{id:"minecraft:paper"}]""",
            """data remove entity @s HandItems[{tag:{display:{Name:'Guard'}}}]""",
            "data modify storage test:state {} merge value {CustomName:'Guard'}"
        };
        var accepted = unsupported.Where(command => CommandTextExtractor.Extract(new(Source, command), profile).Complete).ToArray();
        Require(accepted.Length == 0, "wrong grammar and unbound payloads stay unresolved: " + string.Join(", ", accepted));
    }

    public static void InertFieldsCannotBecomeReferences()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var component = modern ? "{text:'Hi',with:[{selector:'@e[name=Guard]'}],separator:{selector:'@e[name=Guard]'}}"
                : "{\"text\":\"Hi\",\"with\":[{\"selector\":\"@e[name=Guard]\"}],\"separator\":{\"selector\":\"@e[name=Guard]\"}}";
            var inert = CommandTextExtractor.Extract(new(Source, "tellraw @s " + component), MinecraftProfiles.ForDataVersion(version)!);
            Require(inert.Complete && inert.References.IsEmpty && inert.Text.Occurrences.Single().Text == "Hi", "inert with/separator on literal text are protected, not authorized name references");
            var active = modern ? "{translate:'key',with:[{selector:'@e[name=Guard]'}]}" : "{\"translate\":\"key\",\"with\":[{\"selector\":\"@e[name=Guard]\"}]}";
            Require(CommandTextExtractor.Extract(new(Source, "tellraw @s " + active), MinecraftProfiles.ForDataVersion(version)!).References.Single().Name == "Guard", "active translate.with keeps its selector reference");
            var separator = modern ? "{selector:'@s',separator:{selector:'@e[name=Guard]'}}" : "{\"selector\":\"@s\",\"separator\":{\"selector\":\"@e[name=Guard]\"}}";
            Require(CommandTextExtractor.Extract(new(Source, "tellraw @s " + separator), MinecraftProfiles.ForDataVersion(version)!).References.Single().Name == "Guard", "active selector.separator keeps its reference");
        }
    }
    public static void SelectorTypesAreValidated()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4325, true), (4774, true) })
        {
            var invalid = modern ? "{selector:42}" : "{\"selector\":42}";
            var result = CommandTextExtractor.Extract(new(Source, "tellraw @s " + invalid), MinecraftProfiles.ForDataVersion(version)!);
            Require(!result.Complete && result.Text.Occurrences.IsEmpty && result.References.IsEmpty && result.Text.Diagnostics.Single().Code == "InvalidComponent", "wrong selector type is failed component data, not proof of no text/reference");
            var componentSource = new SourceAddress(Source.Artifact, TypedPath.Root, "text");
            var direct = TextComponentExtractor.Json(componentSource, Encoding.UTF8.GetBytes("{\"selector\":42}"), MinecraftProfiles.ForDataVersion(version)!);
            Require(!direct.Complete && direct.Diagnostics.Single().Code == "InvalidComponent", "shared native component parser rejects selector type before command reference analysis");
        }
    }
    public static void BothSelectorQuotesAndEscapes()
    {
        foreach (var version in new[] { 3953, 4325, 4774 })
            foreach (var quote in new[] { '\'', '"' })
                foreach (var negated in new[] { false, true })
                {
                    var name = "雪" + quote + "Guard"; var token = quote + "雪\\" + quote + "Guard" + quote;
                    var body = "tellraw @a[name=" + (negated ? "!" : "") + token + "] " + (version == 3953 ? "\"Text\"" : "'Text'");
                    var result = CommandTextExtractor.Extract(new(Source, body), MinecraftProfiles.ForDataVersion(version)!);
                    Require(result.Complete && result.References.Length == 1 && result.References[0].Name == name && result.References[0].Negated == negated && result.Text.Occurrences.Single().Text == "Text",
                        "Brigadier quote kind and escape are decoded without dropping supported display text");
                    Require(result.References[0].Address.Syntax!.StartByte == Encoding.UTF8.GetByteCount(body[..body.IndexOf(token, StringComparison.Ordinal)]) && result.References[0].Address.Syntax!.LengthBytes == Encoding.UTF8.GetByteCount(token), "both quote kinds keep exact encoded name-token spans");
                }
    }
    public static void SourceTokenBudgetIsCumulative()
    {
        var profile = MinecraftProfiles.ForDataVersion(3953)!;
        Require(CommandTextExtractor.Extract(new(Source, "give @s paper"), profile, new(MaxNodes: 3)).Complete, "one line with exactly three command tokens is a positive boundary control");
        BinaryNbtTests.RejectCodec(CodecError.LimitExceeded, () => CommandTextExtractor.Extract(new(Source, "give @s paper\ngive @s paper\ngive @s paper"), profile, new(MaxNodes: 3)));
        Require(CommandTextExtractor.Extract(new(Source, "# irrelevant tokens \"[\ngive @s paper\n\n"), profile, new(MaxNodes: 3)).Complete, "comments and blank lines do not consume grammar tokens");
    }
    public static void UnresolvedReferencesStayUnsupported()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":71,\"description\":\"Pack\"}}"));
            const string path = "datapacks/p/data/test/function/book.mcfunction";
            Put(root, path, Encoding.UTF8.GetBytes("give @s written_book[written_book_content={title:'Title',author:'Author',pages:[{raw:{selector:'@e[name=Guard]'}}]}]"));
            var row = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [path]))).ReadSources().Single();
            Require(row.Outcome == SourceOutcome.Unsupported && row.DiagnosticCode == "UnresolvedItemComponentReferences" && row.Commands!.ReferenceCompleteness == ReferenceCompleteness.Unresolved
                && row.Text!.Occurrences.Select(text => text.Text).SequenceEqual(new[] { "Title", "Author" }), "valid but unimplemented deep reference analysis retains text and unsupported semantics");
        }
        finally { Directory.Delete(root, true); }
    }
}
