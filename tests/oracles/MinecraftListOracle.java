import java.io.*;
import java.lang.reflect.*;
import java.security.MessageDigest;
import java.util.*;

// Runs only NBT tag methods from the pinned Mojang 1.21.5 client, never its entry point.
public final class MinecraftListOracle {
    static Class<?> tag, list, compound;
    static Object number(int value) throws Exception { return Class.forName("uf").getMethod("a", int.class).invoke(null, value); }
    static Object text(String value) throws Exception { return Class.forName("uy").getMethod("a", String.class).invoke(null, value); }
    static Object compound(String key, Object value) throws Exception {
        var constructor = compound.getDeclaredConstructor(Map.class); constructor.setAccessible(true);
        return constructor.newInstance(Map.of(key, value));
    }
    static Object list(Object... values) throws Exception {
        var result = list.getConstructor().newInstance();
        for (var value : values) list.getMethod("add", Object.class).invoke(result, value);
        return result;
    }
    static Object unwrap(Object value) throws Exception {
        var method = list.getDeclaredMethod("a", compound); method.setAccessible(true); return method.invoke(null, value);
    }
    static String fixture(String name, Object value) throws Exception {
        var bytes = new ByteArrayOutputStream(); var output = new DataOutputStream(bytes);
        output.writeByte(9); output.writeUTF("");
        list.getMethod("a", DataOutput.class).invoke(value, output);
        var encoded = bytes.toByteArray();
        var input = new DataInputStream(new ByteArrayInputStream(encoded)); input.readByte(); input.readUTF();
        var type = list.getField("a").get(null);
        var load = type.getClass().getDeclaredMethod("a", DataInput.class, Class.forName("uj")); load.setAccessible(true);
        var account = Class.forName("uj").getMethod("a").invoke(null);
        if (!value.equals(load.invoke(type, input, account)) || input.available() != 0) throw new AssertionError("Mojang readback witness");
        return "{\"id\":\"" + name + "\",\"base64\":\"" + Base64.getEncoder().encodeToString(encoded)
            + "\",\"sha256\":\"" + HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(encoded)) + "\",\"readbackWitness\":true}";
    }
    public static void main(String[] args) throws Exception {
        tag = Class.forName("va"); list = Class.forName("ug"); compound = Class.forName("ua");
        var integer = number(42); var singleEmpty = compound("", integer);
        var ordinary = compound("b", number(3));
        if (unwrap(singleEmpty) != integer || unwrap(ordinary) != ordinary) throw new AssertionError("unwrap witness");
        var cases = List.of(
            fixture("official-example", list(text("a"), ordinary)),
            fixture("sole-empty-key-escaped", list(singleEmpty)),
            fixture("nested-mixed", list(list(number(1), text("two")), singleEmpty)),
            fixture("homogeneous-int", list(number(-1), number(42))),
            fixture("empty", list()));
        System.out.println("{\"oracle\":\"Mojang 1.21.5 ListTag methods (ug)\",\"clientSha1\":\"b88808bbb3da8d9f453694b5d8f74a3396f1a533\","
            + "\"mappingsSha1\":\"57669731d542f98646772e91a0d68628f9827a5c\",\"runtime\":\"" + System.getProperty("java.runtime.version")
            + "\",\"unwrapWitness\":true,\"cases\":[" + String.join(",", cases) + "]}");
    }
}
