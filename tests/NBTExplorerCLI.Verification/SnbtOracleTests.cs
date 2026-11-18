using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using System.Text.Json;
using static NBTExplorerCLI.Verification.SnbtTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtOracleTests
{
    public static void OfficialGrammar(string fixtureRoot, bool modern, bool edges = false, string? variant = null)
    {
        var version = modern ? "1.21.5" : "1.21.4";
        var profile = modern ? SnbtProfile.Java1215 : SnbtProfile.JavaThrough1214;
        var suffix = variant is not null ? "-" + variant : edges ? "-edges" : "";
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "codec", "snbt-" + version + suffix + "-oracle.json")));
        var errors = new List<string>();
        foreach (var row in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            var source = row.GetProperty("sourceBase64").GetBytesFromBase64();
            var description = System.Text.Encoding.UTF8.GetString(source);
            if (!row.GetProperty("accepted").GetBoolean())
            {
                try { Snbt.ParseDocument(source, profile); }
                catch (SnbtException) { continue; }
                errors.Add("官方拒絕但本機接受: " + description); continue;
            }
            var expected = BinaryNbt.ReadDocument(row.GetProperty("namedNbtBase64").GetBytesFromBase64()).Document;
            var mapping = MinecraftNbtAdapterTests.Profile(modern ? LogicalListEncoding.CompoundWrapper : LogicalListEncoding.Homogeneous);
            var value = MinecraftNbtAdapter.Decode(expected, mapping).Root;
            try
            {
                var parsed = Snbt.ParseDocument(source, profile);
                if (parsed.Root.Value != value || parsed.Root.Span.EndByte != row.GetProperty("consumedBytes").GetInt32())
                    errors.Add("typed value/span不符: " + description + " actualEnd=" + parsed.Root.Span.EndByte);
                if (Snbt.ParseDocument(Snbt.Write(value, profile), profile).Root.Value != value) errors.Add("writer roundtrip不符: " + description);
            }
            catch (SnbtException error) { errors.Add("官方接受但本機拒絕: " + description + " " + error.Code); }
        }
        Require(errors.Count == 0, string.Join("; ", errors));
    }
}
