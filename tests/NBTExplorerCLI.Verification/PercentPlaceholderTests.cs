using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class PercentPlaceholderTests
{
    public static void IndexedArgumentsAndLiteralFallback()
    {
        Require(PercentPlaceholders.Equivalent("A %s and %s", "%2$s 與 %1$s"), "explicit argument indices can reorder the exact same implicit source arguments");
        Require(PercentPlaceholders.Equivalent("%2$s %s", "%s %2$s") && PercentPlaceholders.Equivalent("%01$s", "%1$s"), "mixed implicit/explicit indices follow independent implicit counter and positive decimal indexing");
        Require(PercentPlaceholders.Equivalent("%% %s", "%1$s %%"), "escaped percent is literal text and does not consume an argument");
        Require(!PercentPlaceholders.Equivalent("%s %s", "%1$s %1$s") && !PercentPlaceholders.Equivalent("%s %s", "%s")
            && !PercentPlaceholders.Equivalent("%s", "%s %2$s") && !PercentPlaceholders.Equivalent("%s", "%q"), "missing, duplicated, added or invalid format references cannot replace the source contract");
        Require(!PercentPlaceholders.Equivalent("%s", "%2147483648$s") && !PercentPlaceholders.Equivalent("%１$s", "%s"), "overflow or non-ASCII index spelling follows literal fallback, never an invented argument");
        Require(PercentPlaceholders.Equivalent("literal {{T0}} %", "字面 {{T1}}") && PercentPlaceholders.Equivalent("%0$s", "zero"), "unparseable source format falls back to literal text, with no active argument contract");
        Require(PercentPlaceholders.Equivalent("%d and %.2f", "%2$s 與 %1$s", true)
            && !PercentPlaceholders.Equivalent("%d and %.2f", "%2$s 與 %1$s"), "language numeric normalization is explicit and distinct from raw component fallback grammar");
        Require(!PercentPlaceholders.Equivalent("%d", "plain", true) && PercentPlaceholders.Equivalent("%d", "plain"), "numeric language arguments are protected while a raw invalid component fallback remains literal");
    }
    public static void IndependentJavaReference(string fixtureRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, "codec", "percent-placeholders.reference.json")));
        var count = 0;
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var source = NativeJsonStrings.Read(item.GetProperty("source")); var target = NativeJsonStrings.Read(item.GetProperty("target")); var numeric = item.GetProperty("normalizeNumbers").GetBoolean();
            Require(PercentPlaceholders.Equivalent(source, target, numeric) == item.GetProperty("equivalent").GetBoolean(), "C# bounded scanner differs from independent Java Pattern reference pair " + count);
            count++;
        }
        Require(count == 2312, "entire fixed independent percent grammar reference is checked");
    }
}
