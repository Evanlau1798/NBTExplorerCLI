using System.Globalization;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private static int ScoreCondition(List<CommandToken> tokens, int cursor)
    {
        if (cursor + 4 >= tokens.Count || !ScoreHolder(tokens[cursor + 1].Text) || !PlainControlToken(tokens[cursor + 2].Text)) return -1;
        var comparison = tokens[cursor + 3].Text;
        if (comparison == "matches") return IntegerRange(tokens[cursor + 4].Text) ? 5 : -1;
        return comparison is "<" or "<=" or "=" or ">=" or ">" && cursor + 5 < tokens.Count
            && ScoreHolder(tokens[cursor + 4].Text) && PlainControlToken(tokens[cursor + 5].Text) ? 6 : -1;
    }

    private static bool StaticControlCommand(List<CommandToken> tokens, int index, string name)
    {
        var count = tokens.Count - index;
        string Arg(int offset) => offset < count ? tokens[index + offset].Text : "";
        bool Position(int offset) => Coordinates(Arg(offset), Arg(offset + 1), Arg(offset + 2));
        switch (name)
        {
            case "tp" or "teleport":
                return count == 5 && EntityTarget(Arg(1)) && Position(2);
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
                    && ResourceLocation(Arg(1)) && Arg(2) is "master" or "music" or "record" or "weather" or "block" or "hostile" or "neutral" or "player" or "ambient" or "voice"
                    && EntityTarget(Arg(3));
            case "fill":
                return (count == 8 || count == 9 && Arg(8) is "replace" or "destroy" or "keep" or "hollow" or "outline")
                    && BlockCoordinates(Arg(1), Arg(2), Arg(3)) && BlockCoordinates(Arg(4), Arg(5), Arg(6)) && ResourceLocation(Arg(7));
            case "particle":
                return MinecraftIdentifier.DefaultNamespace(Arg(1)) is "minecraft:flame" or "minecraft:smoke" or "minecraft:dragon_breath"
                    && (count == 2 || count == 5 && Position(2) || count is >= 10 and <= 12 && Position(2)
                    && tokens.Skip(index + 5).Take(3).All(token => Decimal(token.Text, out _))
                    && Decimal(Arg(8), out var speed) && speed >= 0 && Integer(Arg(9), out var amount) && amount >= 0
                    && (count == 10 || Arg(10) is "normal" or "force") && (count < 12 || EntityTarget(Arg(11))));
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

    private static int BlockOrItemCondition(List<CommandToken> tokens, int cursor)
    {
        string Arg(int offset) => cursor + offset < tokens.Count ? tokens[cursor + offset].Text : "";
        bool Item(string text) => text == "*" || ResourceOrTag(text);
        if (Arg(0) == "block" && BlockCoordinates(Arg(1), Arg(2), Arg(3)) && ResourceOrTag(Arg(4))) return 5;
        if (Arg(0) != "items") return -1;
        if (Arg(1) == "block" && BlockCoordinates(Arg(2), Arg(3), Arg(4)) && PlainControlToken(Arg(5)) && Item(Arg(6))) return 7;
        return Arg(1) == "entity" && EntityTarget(Arg(2)) && PlainControlToken(Arg(3)) && Item(Arg(4)) ? 5 : -1;
    }

    private static bool SelectorTarget(string text) => text.Length >= 2 && text[0] == '@'
        && text[1] is 'a' or 'e' or 'n' or 'p' or 'r' or 's' && (text.Length == 2 || text[2] == '[');
    private static bool EntityTarget(string text) => text.StartsWith('@')
        ? SelectorTarget(text)
        : text.Length is >= 1 and <= 16 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') || Guid.TryParseExact(text, "D", out _);
    private static bool ScoreHolder(string text, bool allowWildcard = false)
        => SelectorTarget(text) || allowWildcard && text == "*" || PlainControlToken(text);
    private static bool PlainControlToken(string text) => text.Length != 0 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':' or '#');

    internal static bool ResourceOrTag(string text) => ResourceLocation(text.StartsWith('#') ? text[1..] : text);
    private static bool ResourceLocation(string text)
    {
        var separator = text.IndexOf(':');
        if (separator >= 0 && !text.AsSpan(0, separator).ToString().All(ResourceCharacter)) return false;
        var resource = text[(separator + 1)..];
        return resource.Length != 0 && resource.All(ch => ch == '/' || ResourceCharacter(ch));
    }

    private static bool ResourceCharacter(char ch) => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch is '_' or '-' or '.';

    private static bool Coordinates(string x, string y, string z) => Coordinate(x) && Coordinate(y) && Coordinate(z)
        && x.StartsWith('^') == y.StartsWith('^') && x.StartsWith('^') == z.StartsWith('^');
    private static bool BlockCoordinates(string x, string y, string z) => x.StartsWith('^')
        ? Coordinates(x, y, z) : BlockCoordinate(x) && BlockCoordinate(y) && BlockCoordinate(z);
    private static bool BlockCoordinate(string text) => text == "~" || Integer(text.StartsWith('~') ? text[1..] : text, out _);

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
