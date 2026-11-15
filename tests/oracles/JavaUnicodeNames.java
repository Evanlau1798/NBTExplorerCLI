import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.GZIPOutputStream;

// Generates data, not JDK code. Run with Java 21 (Unicode 15.0).
public final class JavaUnicodeNames {
    public static void main(String[] args) throws Exception {
        if (Runtime.version().feature() != 21) throw new IllegalArgumentException("Use Java 21 Unicode 15.0");
        var text = new StringBuilder(); int count = 0;
        for (int point = 0; point <= Character.MAX_CODE_POINT; point++) {
            var name = Character.getName(point);
            if (name == null) continue;
            if (Character.codePointOf(name) != point) throw new AssertionError("name is not reversible");
            text.append(Integer.toHexString(point)).append('\t').append(name).append('\n'); count++;
        }
        var payload = text.toString().getBytes(StandardCharsets.UTF_8);
        var bytes = new ByteArrayOutputStream();
        try (var gzip = new GZIPOutputStream(bytes)) { gzip.write(payload); }
        System.out.println("{\"unicodeVersion\":\"15.0\",\"runtime\":\"" + System.getProperty("java.runtime.version")
            + "\",\"entries\":" + count + ",\"payloadSha256\":\"" + HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(payload))
            + "\",\"gzipBase64\":\"" + Base64.getEncoder().encodeToString(bytes.toByteArray()) + "\"}");
    }
}
