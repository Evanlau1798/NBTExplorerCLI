using NBTExplorerCLI.Domain;
using System.Globalization;

namespace NBTExplorerCLI.Verification;

internal static class PlanGoldenTests
{
    public static void Vectors(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var plan = PlanTests.Plan();
            var prepared = new PreparedManifest(plan, [PlanTests.Candidate()]);
            TextSlotTests.Require(plan.PlanHash.Hex == "a585d8215d3065142535c16cc4253d8c0c1fab55542b623192685b197f9a40f7"
                && prepared.ManifestHash.Hex == "b1b6027e33585f7c120cc097b1a6e28c2a09f161beb7b5bda65a0f88509fb658",
                culture + ": plan/prepared canonical mismatch with independent Python struct/hashlib vectors");
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
