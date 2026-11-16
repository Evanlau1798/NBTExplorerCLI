import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import java.util.TreeMap;
import java.util.regex.Pattern;

// Project-authored reference: constants checked in official Language and TranslatableContents bytecode.
public final class PercentPlaceholderOracle {
    private static final Pattern NUMERIC = Pattern.compile("%(\\d+\\$)?[\\d.]*[df]");
    private static final Pattern ARGUMENT = Pattern.compile("%(?:(\\d+)\\$)?([A-Za-z%]|$)");
    private static Map<Integer, Integer> arguments(String text, boolean numeric) {
        if (numeric) text = NUMERIC.matcher(text).replaceAll("%$1s");
        var result = new TreeMap<Integer, Integer>();
        var matcher = ARGUMENT.matcher(text); int previous = 0, implicit = 0;
        try {
            while (matcher.find()) {
                if (text.substring(previous, matcher.start()).indexOf('%') >= 0) throw new IllegalArgumentException();
                String token = matcher.group(), conversion = matcher.group(2);
                if (!(conversion.equals("%") && token.equals("%%"))) {
                    if (!conversion.equals("s")) throw new IllegalArgumentException();
                    int index = matcher.group(1) == null ? implicit++ : Integer.parseInt(matcher.group(1)) - 1;
                    if (index < 0) throw new IllegalArgumentException();
                    result.merge(index + 1, 1, Integer::sum);
                }
                previous = matcher.end();
            }
            if (text.substring(previous).indexOf('%') >= 0) throw new IllegalArgumentException();
            return result;
        } catch (IllegalArgumentException error) { return Map.of(); }
    }
    private static String quote(String value) {
        var result = new StringBuilder("\"");
        for (int index = 0; index < value.length(); index++) {
            char unit = value.charAt(index);
            if (unit == '"' || unit == '\\') result.append('\\').append(unit);
            else if (unit < 0x20 || Character.isSurrogate(unit)) result.append(String.format(java.util.Locale.ROOT, "\\u%04x", (int)unit));
            else result.append(unit);
        }
        return result.append('"').toString();
    }
    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("Expected output fixture path");
        String[] samples = { "", "literal {{T0}}", "中文", "x\ud800 %s", "%s", "%s %s", "%2$s %1$s", "%2$s %s", "%s %2$s", "%1$s %1$s",
            "%%", "%% %s", "%%d%s", "%d and %.2f", "%1.2.3d", "%1$.2f", "%01$s", "%0$s", "%2147483647$s", "%2147483648$s", "%１$s", "%١$s",
            "%q", "%", "%1$", "%$s", "%1$%", "%%%s", "%1$-2d", "%1$1.2.3d", "%.f", "%s %q", "%\ns", "%%s" };
        var json = new StringBuilder("{\"schemaVersion\":1,\"basis\":\"Java Pattern and Integer; four official Minecraft formatter profiles\",\"cases\":[");
        int count = 0;
        for (boolean numeric : new boolean[] { false, true }) for (String source : samples) for (String target : samples) {
            if (count++ > 0) json.append(',');
            json.append("{\"source\":").append(quote(source)).append(",\"target\":").append(quote(target)).append(",\"normalizeNumbers\":").append(numeric)
                .append(",\"equivalent\":").append(arguments(source, numeric).equals(arguments(target, numeric))).append('}');
        }
        json.append("]}\n"); Files.writeString(Path.of(args[0]), json, StandardCharsets.UTF_8);
        System.out.println("Wrote " + count + " reference pairs");
    }
}
