import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import com.mojang.brigadier.StringReader;
import com.mojang.brigadier.exceptions.CommandSyntaxException;

// Uses pinned official TagParser methods; never calls a game/server entry point.
public final class MinecraftSnbtOracle {
    static boolean modern;
    static Class<?> parserClass;
    static Object parser;
    static String encoded(byte[] bytes) { return Base64.getEncoder().encodeToString(bytes); }
    static Object parse(StringReader reader) throws Exception {
        if (modern) return parserClass.getMethod("b", StringReader.class).invoke(parser, reader);
        var java1214Parser = parserClass.getConstructor(StringReader.class).newInstance(reader);
        return parserClass.getMethod("d").invoke(java1214Parser);
    }
    static String fixture(String text) throws Exception {
        var prefix = "{\"sourceBase64\":\"" + encoded(text.getBytes(StandardCharsets.UTF_8)) + "\",";
        var reader = new StringReader(text);
        try {
            var value = parse(reader);
            int consumed = text.substring(0, reader.getCursor()).getBytes(StandardCharsets.UTF_8).length;
            reader.skipWhitespace();
            if (reader.canRead()) return prefix + "\"accepted\":false,\"error\":\"TrailingInput\"}";
            var bytes = new ByteArrayOutputStream(); var output = new DataOutputStream(bytes);
            output.writeByte((byte)value.getClass().getMethod("b").invoke(value)); output.writeUTF("");
            value.getClass().getMethod("a", DataOutput.class).invoke(value, output);
            return prefix + "\"accepted\":true,\"consumedBytes\":" + consumed + ",\"namedNbtBase64\":\"" + encoded(bytes.toByteArray()) + "\"}";
        } catch (InvocationTargetException error) {
            if (!(error.getCause() instanceof CommandSyntaxException)) throw error;
            return prefix + "\"accepted\":false,\"error\":\"CommandSyntaxException\"}";
        }
    }
    public static void main(String[] args) throws Exception {
        modern = args.length >= 1 && args[0].equals("1.21.5");
        parserClass = Class.forName(modern ? "vb" : "uo");
        if (modern) { var field = parserClass.getDeclaredField("g"); field.setAccessible(true); parser = field.get(null); }
        String[] cases = {"01", "01b", "1e3", "1.e3", ".1e3", "128b", "2147483648", "1e9999d", "{\"\":1}", "{'minecraft:key':1}",
            "[1,]", "{a:1,}", "[B;1]", "[B;-1b]", "[I;1b,2s,3]", "[L;1,2i]", "[I;1L]", "[ B;1b]", "[1,'two',{keep:42}]",
            "'\\n'", "'a\\'b'", "'\\x42\\u2603\\U0001F600'", "'\\N{Snowman}'", "'\\uD800'", "'\\q'", "minecraft:stone",
            "0xFFub", "240ub", "240sb", "0xFFFFFFFF", "-0x1", "0b10_01", "1_2.3_4__5f", "1_2e3_4", "1e1000", "NaN", "Infinityf",
            "bool(0)", "bool(-2.5)", "bool(true)", "bool('string')", "uuid('00000000-0000-0000-0000-000000000001')", "unknown(1)", "[1,,]"};
        if (args.length > 1) cases = new String[] {"1 f", "1 e3", "- 1", "+ 1", "0x FF", "1_", "_1", "1__2", "1_.2", "1._2", "1e_3", "1e3_", "1e + 3", "1 ",
            "0XFF", "+0xff", "-0ui", "0x11b", "0b10b", "0x80sb", "-0x80sb", "0x100ub", "18446744073709551615ul", "18446744073709551616ul",
            "{1:2}", "{true:2}", "{a/b:1}", "a/b", "TRUE", "[ B ; 1]", "[L;0xffffffff]", "[L;0xffffffffi]", "[I;255ub]", "[B;128]",
            "'\\\"'", "\"line\nraw\"", "'\\N{ SNOWMAN }'", "'\\N{HANGUL SYLLABLE GA}'", "'\\N{CJK UNIFIED IDEOGRAPHS 4E00}'", "'\\N{not a name}'", "'\\U00110000'",
            "bool(-0.0d)", "uuid('1-1-1-1-1')", "uuid('bad')", "bool(1,2)", "[B;true,false]", "1 foo", "[1 2]", "[I;]", "{}",
            "0b", "0B", "0b1", "0b0", "0b2", "0b ", "0b 1", "[0b]", "1. ", "uuid(' 1-1-1-1-1')", "1 u b", "01.0", "00e1"};
        if (args.length > 1 && args[1].equals("findings")) cases = new String[] {
            "bool(0.5)", "bool(-0.5)", "bool(256)", "bool(-256)", "bool(257)", "bool(65536)", "bool(256s)", "bool(256l)", "bool(256.5f)",
            "bool(2147483648d)", "bool(-2147483649d)", "bool(1e100)", "bool(-1e100)", "bool(255ub)", "bool(-255.5f)",
            "-0f", "-0d", "-0.0f", "-0.0d", "-0.0", "-0e0f", "-0.0e0f", "-0.f", "-.0f", "-1e-500d", "0.0f", "0.0d",
            "uuid('１-１-１-１-１')", "uuid('١-١-١-١-١')", "uuid('Ａ-Ｂ-Ｃ-Ｄ-Ｅ')", "uuid('ａ-ｂ-ｃ-ｄ-ｅ')", "uuid('+1-+1-+1-+1-+1')",
            "uuid('ffffffff-ffff-ffff-ffff-ffffffffffff')", "uuid('ffffffffffffffff-1-1-1-1')", "uuid('²-1-1-1-1')", "uuid(' 1-1-1-1-1')"};
        var rows = new ArrayList<String>();
        for (var text : cases) rows.add(fixture(text));
        System.out.println("{\"oracle\":\"Mojang TagParser\",\"version\":\"" + (modern ? "1.21.5" : "1.21.4")
            + "\",\"runtime\":\"" + System.getProperty("java.runtime.version") + "\",\"cases\":[" + String.join(",", rows) + "]}");
    }
}
