using System.Globalization;
using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Minecraft.MinecraftIdentifier;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private int ScoreCondition(List<CommandToken> tokens, int cursor)
    {
        if (cursor + 4 >= tokens.Count || !ScoreHolder(tokens[cursor + 1].Text) || !PlainControlToken(tokens[cursor + 2].Text)) return -1;
        var comparison = tokens[cursor + 3].Text;
        if (comparison == "matches") return IntegerRange(tokens[cursor + 4].Text) ? 5 : -1;
        return comparison is "<" or "<=" or "=" or ">=" or ">" && cursor + 5 < tokens.Count
            && ScoreHolder(tokens[cursor + 4].Text) && PlainControlToken(tokens[cursor + 5].Text) ? 6 : -1;
    }

    private bool StaticControlCommand(List<CommandToken> tokens, int index, string name)
    {
        var count = tokens.Count - index;
        string Arg(int offset) => offset < count ? tokens[index + offset].Text : "";
        bool Position(int offset) => Coordinates(Arg(offset), Arg(offset + 1), Arg(offset + 2));
        switch (name)
        {
            case "tp" or "teleport":
                if (count is 2 or 3) return EntityTarget(Arg(1)) && (count == 2 || EntityTarget(Arg(2)));
                if (count == 4) return Position(1);
                if (!EntityTarget(Arg(1)) || !Position(2)) return false;
                return count == 5 || count == 7 && Angle(Arg(5)) && Angle(Arg(6))
                    || Arg(5) == "facing" && (count == 9 && Position(6)
                        || count is 8 or 9 && Arg(6) == "entity" && EntityTarget(Arg(7)) && (count == 8 || Arg(8) is "eyes" or "feet"));
            case "clone":
                return (count == 10 || count is 11 or 12 && Arg(10) is "replace" or "masked"
                    && (count == 11 || Arg(11) is "normal" or "force" or "move")
                    || count is 12 or 13 && Arg(10) == "filtered" && StaticBlock(Arg(11), allowTag: true)
                    && (count == 12 || Arg(12) is "normal" or "force" or "move"))
                    && Enumerable.Range(0, 3).All(group => BlockCoordinates(Arg(1 + 3 * group), Arg(2 + 3 * group), Arg(3 + 3 * group)));
            case "advancement":
                if (Arg(1) is not ("grant" or "revoke") || !EntityTarget(Arg(2))) return false;
                if (Arg(2).StartsWith('@') && Arg(2)[1] is not ('a' or 'p' or 'r' or 's')
                    && selectorTypes.GetValueOrDefault(tokens[index + 2].Start) != "minecraft:player") return false;
                return Arg(3) switch
                {
                    "everything" => count == 4,
                    "from" or "until" or "through" => count == 5 && ResourceLocation(Arg(4)),
                    "only" => count is 5 or 6 && ResourceLocation(Arg(4)) && (count == 5 || PlainControlToken(Arg(5))),
                    _ => false
                };
            case "tag":
                return EntityTarget(Arg(1)) && (count == 3 && Arg(2) == "list" || count == 4 && Arg(2) is "add" or "remove" && PlainControlToken(Arg(3)));
            case "effect":
                return EntityTarget(Arg(2)) && (Arg(1) == "clear" && (count == 3 || count == 4 && ResourceLocation(Arg(3)))
                    || Arg(1) == "give" && count is >= 4 and <= 7 && ResourceLocation(Arg(3))
                    && (count < 5 || Arg(4) == "infinite" || Integer(Arg(4), out var seconds) && seconds >= 1)
                    && (count < 6 || Integer(Arg(5), out var amplifier) && amplifier is >= 0 and <= 255)
                    && (count < 7 || Arg(6) is "true" or "false"));
            case "playsound":
                return (count == 4 || count is >= 7 and <= 10 && Position(4)
                    && tokens.Skip(index + 7).All(token => Decimal(token.Text, out var value) && value >= 0))
                    && ResourceLocation(Arg(1)) && SoundChannel(Arg(2)) && EntityTarget(Arg(3));
            case "stopsound":
                return count is >= 2 and <= 4 && EntityTarget(Arg(1))
                    && (count == 2 || SoundChannel(Arg(2)) || count == 4 && Arg(2) == "*")
                    && (count < 4 || ResourceLocation(Arg(3)));
            case "gamerule":
                return count is 2 or 3 && profile.Id == "java-1.20.1" && KnownGameRule(Arg(1), count == 2 ? null : Arg(2));
            case "clear":
                return count is >= 1 and <= 4 && (count == 1 || EntityTarget(Arg(1)))
                    && (count < 3 || ResourceOrTag(Arg(2)))
                    && (count < 4 || Integer(Arg(3), out var maximumItems) && maximumItems >= 0);
            case "team":
                return Arg(1) switch
                {
                    "list" => count == 2 || count == 3 && PlainControlToken(Arg(2)),
                    "add" or "remove" or "empty" => count == 3 && PlainControlToken(Arg(2)),
                    "join" => PlainControlToken(Arg(2)) && (count == 3 || count == 4 && ScoreHolder(Arg(3))),
                    "leave" => count == 3 && ScoreHolder(Arg(2)),
                    "modify" => count == 5 && PlainControlToken(Arg(2)) && TeamOption(Arg(3), Arg(4)),
                    _ => false
                };
            case "fill":
                return (count == 8 || count == 9 && Arg(8) is "replace" or "destroy" or "keep" or "hollow" or "outline"
                    || count == 10 && Arg(8) == "replace" && StaticBlock(Arg(9), allowTag: true))
                    && BlockCoordinates(Arg(1), Arg(2), Arg(3)) && BlockCoordinates(Arg(4), Arg(5), Arg(6)) && StaticBlock(Arg(7));
            case "damage":
                return EntityTarget(Arg(1)) && Decimal(Arg(2), out var amount) && amount >= 0
                    && (count == 3 || ResourceLocation(Arg(3)) && (count == 4
                        || Arg(4) == "by" && EntityTarget(Arg(5)) && (count == 6 || count == 8 && Arg(6) == "from" && EntityTarget(Arg(7)))
                        || count == 8 && Arg(4) == "at" && Position(5)));
            case "return":
                return count == 2 && Integer(Arg(1), out _);
            case "particle":
                var particle = DefaultNamespace(Arg(1));
                var positionalPayload = profile.Id == "java-1.20.1";
                var position = particle switch
                {
                    "minecraft:block" or "minecraft:block_marker" or "minecraft:falling_dust" =>
                        positionalPayload && StaticBlock(Arg(2)) ? 3 : -1,
                    "minecraft:dust" => positionalPayload && count >= 6 && tokens.Skip(index + 2).Take(4).All(token => Decimal(token.Text, out _)) ? 6 : -1,
                    "minecraft:dust_color_transition" => positionalPayload && count >= 9 && tokens.Skip(index + 2).Take(7).All(token => Decimal(token.Text, out _)) ? 9 : -1,
                    "minecraft:item" or "minecraft:vibration" or "minecraft:shriek" or "minecraft:sculk_charge" => -1,
                    _ => ResourceLocation(Arg(1)) ? 2 : -1
                };
                return position >= 0 && (count == position || count == position + 3 && Position(position)
                    || count >= position + 8 && count <= position + 10 && Position(position)
                    && tokens.Skip(index + position + 3).Take(3).All(token => Decimal(token.Text, out _))
                    && Decimal(Arg(position + 6), out var speed) && speed >= 0 && Integer(Arg(position + 7), out var particleCount) && particleCount >= 0
                    && (count == position + 8 || Arg(position + 8) is "normal" or "force")
                    && (count < position + 10 || EntityTarget(Arg(position + 9))));
            case "title":
                return count == 6 && EntityTarget(Arg(1)) && Arg(2) == "times" && Integer(Arg(3), out _) && Integer(Arg(4), out _) && Integer(Arg(5), out _);
            case "kill":
                return count == 2 && EntityTarget(Arg(1));
            case "function":
                return count == 2 && ResourceOrTag(Arg(1));
            case "loot":
                return count == 5 && Arg(1) == "give" && EntityTarget(Arg(2)) && Arg(3) == "loot" && ResourceLocation(Arg(4));
            case "time":
                return count == 3 && Arg(1) == "query" && Arg(2) is "day" or "daytime" or "gametime";
            case "bossbar":
                if (Arg(1) != "set" || !ResourceLocation(Arg(2))) return false;
                return Arg(3) switch
                {
                    "color" => count == 5 && Arg(4) is "pink" or "blue" or "red" or "green" or "yellow" or "purple" or "white",
                    "style" => count == 5 && Arg(4) is "progress" or "notched_6" or "notched_10" or "notched_12" or "notched_20",
                    "max" => count == 5 && Integer(Arg(4), out var max) && max >= 1,
                    "value" => count == 5 && Integer(Arg(4), out var value) && value >= 0,
                    "visible" => count == 5 && Arg(4) is "true" or "false",
                    "players" => count == 4 || count == 5 && EntityTarget(Arg(4)),
                    _ => false
                };
            case "scoreboard":
                if (Arg(1) == "objectives") return Arg(2) switch
                {
                    "add" => count == 5 && PlainControlToken(Arg(3)) && PlainControlToken(Arg(4)),
                    "setdisplay" => PlainControlToken(Arg(3)) && (count == 4 || count == 5 && PlainControlToken(Arg(4))),
                    _ => false
                };
                if (Arg(1) != "players" || !ScoreHolder(Arg(3), allowWildcard: Arg(2) != "get")) return false;
                return Arg(2) switch
                {
                    "add" or "remove" or "set" => count == 6 && PlainControlToken(Arg(4)) && Integer(Arg(5), out _),
                    "get" => count == 5 && PlainControlToken(Arg(4)),
                    "reset" => count == 4 || count == 5 && PlainControlToken(Arg(4)),
                    "operation" => count == 8 && PlainControlToken(Arg(4)) && Arg(5) is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "<" or ">" or "><"
                        && ScoreHolder(Arg(6), allowWildcard: true) && PlainControlToken(Arg(7)),
                    _ => false
                };
            default:
                return false;
        }
    }

    private int BlockOrItemCondition(List<CommandToken> tokens, int cursor)
    {
        string Arg(int offset) => cursor + offset < tokens.Count ? tokens[cursor + offset].Text : "";
        bool Item(string text) => text == "*" || ResourceOrTag(text);
        if (Arg(0) == "block" && BlockCoordinates(Arg(1), Arg(2), Arg(3)) && StaticBlock(Arg(4), allowTag: true)) return 5;
        if (Arg(0) == "blocks" && Arg(10) is "all" or "masked"
            && Enumerable.Range(0, 3).All(group => BlockCoordinates(Arg(1 + group * 3), Arg(2 + group * 3), Arg(3 + group * 3)))) return 11;
        if (Arg(0) != "items" || !MinecraftProfiles.SupportsItemConditions(profile)) return -1;
        if (Arg(1) == "block" && BlockCoordinates(Arg(2), Arg(3), Arg(4)) && ItemSlots(Arg(5)) && Item(Arg(6))) return 7;
        return Arg(1) == "entity" && EntityTarget(Arg(2)) && ItemSlots(Arg(3)) && Item(Arg(4)) ? 5 : -1;
    }
    private static bool ItemSlots(string value) => PlainControlToken(value)
        || value.EndsWith(".*", StringComparison.Ordinal) && PlainControlToken(value[..^2]);

    private static bool SoundChannel(string value) => value is "master" or "music" or "record" or "weather" or "block" or "hostile"
        or "neutral" or "player" or "ambient" or "voice";
    private static bool KnownGameRule(string name, string? value) => name switch
    {
        "randomTickSpeed" or "spawnRadius" or "maxEntityCramming" or "maxCommandChainLength" or "commandModificationBlockLimit"
            or "playersSleepingPercentage" or "snowAccumulationHeight" => value is null || Integer(value, out _),
        "doFireTick" or "mobGriefing" or "keepInventory" or "doMobSpawning" or "doMobLoot" or "doTileDrops" or "doEntityDrops"
            or "commandBlockOutput" or "naturalRegeneration" or "doDaylightCycle" or "logAdminCommands" or "showDeathMessages"
            or "sendCommandFeedback" or "reducedDebugInfo" or "spectatorsGenerateChunks" or "disableElytraMovementCheck" or "doWeatherCycle"
            or "doLimitedCrafting" or "announceAdvancements" or "disableRaids" or "doInsomnia" or "doImmediateRespawn" or "drowningDamage"
            or "fallDamage" or "fireDamage" or "freezeDamage" or "doPatrolSpawning" or "doTraderSpawning" or "doWardenSpawning"
            or "forgiveDeadPlayers" or "universalAnger" or "blockExplosionDropDecay" or "mobExplosionDropDecay" or "tntExplosionDropDecay"
            or "waterSourceConversion" or "lavaSourceConversion" or "globalSoundEvents" or "doVinesSpread" => value is null or "true" or "false",
        _ => false
    };
    private bool SingleItemSlot(string value)
    {
        if (value is "weapon" or "weapon.mainhand" or "weapon.offhand" or "armor.head" or "armor.chest" or "armor.legs" or "armor.feet" or "horse.chest")
            return true;
        var itemTags = profile.Features[ProfileFacet.ItemSchema].Schema == "java-item-tags-1.20.4";
        if (value is "horse.armor" or "horse.saddle") return itemTags;
        if (value is "armor.body" or "player.cursor") return MinecraftProfiles.SupportsItemConditions(profile);
        var split = value.LastIndexOf('.');
        if (split < 0) return false;
        var maximum = value[..split] switch
        {
            "container" => 53, "hotbar" => 8, "inventory" or "enderchest" => 26,
            "horse" => 14, "villager" when itemTags => 7,
            "player.crafting" when MinecraftProfiles.SupportsItemConditions(profile) => 3, _ => -1
        };
        var digits = value.AsSpan(split + 1);
        return (digits.Length == 1 || digits.Length > 1 && digits[0] != '0')
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index <= maximum;
    }
    private static bool TeamOption(string name, string value) => name switch
    {
        "color" => value is "reset" or "black" or "dark_blue" or "dark_green" or "dark_aqua" or "dark_red" or "dark_purple"
            or "gold" or "gray" or "dark_gray" or "blue" or "green" or "aqua" or "red" or "light_purple" or "yellow" or "white",
        "friendlyFire" or "seeFriendlyInvisibles" => value is "true" or "false",
        "collisionRule" => value is "always" or "never" or "pushOtherTeams" or "pushOwnTeam",
        "nametagVisibility" or "deathMessageVisibility" => value is "always" or "never" or "hideForOtherTeams" or "hideForOwnTeam",
        _ => false
    };

    private static bool SelectorTarget(string text) => text.Length >= 2 && text[0] == '@'
        && text[1] is 'a' or 'e' or 'n' or 'p' or 'r' or 's' && (text.Length == 2 || text[2] == '[');
    private static bool EntityTarget(string text) => text.StartsWith('@')
        ? SelectorTarget(text)
        : text.Length is >= 1 and <= 16 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') || Guid.TryParseExact(text, "D", out _);
    private bool ScoreHolder(string text, bool allowWildcard = false)
        => SelectorTarget(text) || allowWildcard && text == "*" || PlainControlToken(text)
            || profile.Id == "java-1.20.1" && text.StartsWith('$') && PlainControlToken(text[1..]);
    private static bool PlainControlToken(string text) => text.Length != 0 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':' or '#');

    internal static bool ResourceOrTag(string text) => ResourceLocation(text.StartsWith('#') ? text[1..] : text);

    private static bool StaticBlock(string text, bool allowTag = false)
    {
        var bracket = text.IndexOf('[');
        var id = bracket < 0 ? text : text[..bracket];
        if (!(allowTag ? ResourceOrTag(id) : ResourceLocation(id))) return false;
        if (bracket < 0) return true;
        if (text[^1] != ']') return false;
        var properties = text[(bracket + 1)..^1].Trim();
        if (properties.Length == 0) return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.Split(','))
        {
            var fields = property.Split('=', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !PlainControlToken(fields[0]) || !PlainControlToken(fields[1]) || !seen.Add(fields[0])) return false;
        }
        return true;
    }

    private static bool Coordinates(string x, string y, string z) => Coordinate(x) && Coordinate(y) && Coordinate(z)
        && x.StartsWith('^') == y.StartsWith('^') && x.StartsWith('^') == z.StartsWith('^');
    private static bool BlockCoordinates(string x, string y, string z) => x.StartsWith('^')
        ? Coordinates(x, y, z) : BlockCoordinate(x) && BlockCoordinate(y) && BlockCoordinate(z);
    private static bool BlockCoordinate(string text) => text.StartsWith('~') ? Coordinate(text) : Integer(text, out _);
    private static bool Angle(string text) => !text.StartsWith('^') && Coordinate(text);

    internal static bool Coordinate(string value)
    {
        var number = value.AsSpan();
        if (number.StartsWith("~") || number.StartsWith("^"))
        {
            number = number[1..];
            if (number.IsEmpty) return true;
        }
        return Decimal(number, out _);
    }

    private static bool Decimal(ReadOnlySpan<char> text, out double value)
        => double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool Integer(string text, out int number) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number) && !text.StartsWith('+');

    private static bool IntegerRange(string text)
    {
        var delimiter = text.IndexOf("..", StringComparison.Ordinal);
        if (delimiter < 0) return Integer(text, out _);
        var minimum = text[..delimiter]; var maximum = text[(delimiter + 2)..];
        if (minimum.Length == 0) return Integer(maximum, out _);
        return Integer(minimum, out var low) && (maximum.Length == 0 || Integer(maximum, out var high) && low <= high);
    }
}
