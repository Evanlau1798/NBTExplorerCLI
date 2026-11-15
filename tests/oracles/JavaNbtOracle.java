import java.io.*;
import java.security.MessageDigest;
import java.util.*;

// Independent fixture author: uses JDK DataInput/DataOutput, never product assemblies.
public final class JavaNbtOracle {
    static String hex(byte[] bytes) { return HexFormat.of().withUpperCase().formatHex(bytes); }
    static String units(String text) {
        var result = new StringBuilder();
        for (int i = 0; i < text.length(); i++) result.append(String.format(Locale.ROOT, "%04X", (int)text.charAt(i)));
        return result.toString();
    }
    static byte[] frame(byte[] payload) throws IOException {
        var bytes = new ByteArrayOutputStream();
        var output = new DataOutputStream(bytes);
        output.writeShort(payload.length); output.write(payload); return bytes.toByteArray();
    }
    static byte[] encode(String text) throws IOException {
        var bytes = new ByteArrayOutputStream(); new DataOutputStream(bytes).writeUTF(text); return bytes.toByteArray();
    }
    static String mutf(String id, byte[] framed) throws IOException {
        var prefix = "{\"id\":\"" + id + "\",\"framedBase64\":\"" + Base64.getEncoder().encodeToString(framed) + "\",";
        try {
            var input = new DataInputStream(new ByteArrayInputStream(framed));
            String decoded = input.readUTF();
            if (input.available() != 0) throw new AssertionError("oracle frame not fully consumed");
            return prefix + "\"accepted\":true,\"utf16be\":\"" + units(decoded) + "\",\"error\":null}";
        } catch (UTFDataFormatException error) {
            return prefix + "\"accepted\":false,\"utf16be\":null,\"error\":\"UTFDataFormatException\"}";
        }
    }
    static String boundary(String id, String text) throws Exception {
        try {
            var framed = encode(text);
            var payload = Arrays.copyOfRange(framed, 2, framed.length);
            return "{\"id\":\"" + id + "\",\"accepted\":true,\"bytes\":" + payload.length
                + ",\"sha256\":\"" + hex(MessageDigest.getInstance("SHA-256").digest(payload)) + "\"}";
        } catch (UTFDataFormatException error) {
            return "{\"id\":\"" + id + "\",\"accepted\":false,\"bytes\":null,\"sha256\":null}";
        }
    }
    static void named(DataOutputStream output, int tag, String name) throws IOException { output.writeByte(tag); output.writeUTF(name); }
    static byte[] nbt() throws IOException {
        var bytes = new ByteArrayOutputStream(); var output = new DataOutputStream(bytes);
        named(output, 10, "root\0");
        named(output, 1, "byte"); output.writeByte(-128);
        named(output, 2, "short"); output.writeShort(-32768);
        named(output, 3, "int"); output.writeInt(2147483647);
        named(output, 4, "long"); output.writeLong(Long.MIN_VALUE);
        named(output, 5, "float"); output.writeInt(0x7fc01234);
        named(output, 6, "double"); output.writeLong(0x8000000000000000L);
        named(output, 7, "bytes"); output.writeInt(4); output.write(new byte[] {-128, -1, 0, 127});
        named(output, 8, "string"); output.writeUTF("zero\0\u0800\ud800\ud83d\ude00");
        named(output, 9, "list"); output.writeByte(3); output.writeInt(3);
        output.writeInt(-1); output.writeInt(0); output.writeInt(2147483647);
        named(output, 9, "empty"); output.writeByte(4); output.writeInt(0);
        named(output, 10, "compound");
        named(output, 8, "type"); output.writeUTF("custom");
        named(output, 8, "value"); output.writeUTF("payload");
        named(output, 3, ""); output.writeInt(42);
        named(output, 2, "duplicate"); output.writeShort(7);
        named(output, 2, "duplicate"); output.writeShort(8); output.writeByte(0);
        named(output, 11, "ints"); output.writeInt(4);
        for (int value : new int[] {Integer.MIN_VALUE, -1, 0, Integer.MAX_VALUE}) output.writeInt(value);
        named(output, 12, "longs"); output.writeInt(4);
        for (long value : new long[] {Long.MIN_VALUE, -1, 0, Long.MAX_VALUE}) output.writeLong(value);
        output.writeByte(0); return bytes.toByteArray();
    }
    public static void main(String[] args) throws Exception {
        var cases = new ArrayList<String>();
        cases.add(mutf("canonical-units", encode("\0\u0001\u007f\u0080\u07ff\u0800\ud800\udc00\uffff\ud83d\ude00")));
        cases.add(mutf("java-permissive", frame(new byte[] {0, (byte)0xc0, (byte)0x81, (byte)0xe0, (byte)0x80, (byte)0x80})));
        String[] bad = {"C2", "C241", "E080", "E08041", "80", "FF", "F09F9880"};
        for (String encoded : bad) cases.add(mutf("malformed-" + encoded, frame(HexFormat.of().parseHex(encoded))));
        var limits = new ArrayList<String>();
        limits.add(boundary("ascii-65535", "a".repeat(65535)));
        limits.add(boundary("ascii-65536", "a".repeat(65536)));
        limits.add(boundary("nul-32767", "\0".repeat(32767)));
        limits.add(boundary("nul-32768", "\0".repeat(32768)));
        var nbt = nbt();
        System.out.println("{\"oracle\":\"JDK DataInputStream/DataOutputStream\",\"runtime\":\"" + System.getProperty("java.runtime.version")
            + "\",\"mutf8\":[" + String.join(",", cases) + "],\"boundaries\":[" + String.join(",", limits)
            + "],\"nbt\":{\"base64\":\"" + Base64.getEncoder().encodeToString(nbt) + "\",\"bytes\":" + nbt.length
            + ",\"sha256\":\"" + hex(MessageDigest.getInstance("SHA-256").digest(nbt)) + "\"}}");
    }
}
