using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class ClickCommandExtractionTests
{
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
                ("/execute unless entity @s[tag=found] run function example:advance", true, null),
                ("/execute if entity @e[type=minecraft:armor_stand,tag=marker] run function #example:advance", true, null),
                ("/execute if entity @e[name=Guard] run function example:advance", false, null),
                ("/execute if entity @s[tag=found] run data modify storage example:state key set value 1", false, null),
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
}
