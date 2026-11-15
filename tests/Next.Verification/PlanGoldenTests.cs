using Next.Domain;
using System.Globalization;

namespace Next.Verification;

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
            TextSlotTests.Require(plan.PlanHash.Hex == "1484b7c07af3ffc3bb1e09e74c097c538854cb484a04c3e8b79cf4d184439160"
                && prepared.ManifestHash.Hex == "2e76b68b64520e31b2f8a22cd9f040e387bea49521a5adfe15b0e3cce049d2ee",
                culture + ": plan/prepared canonical mismatch with independent Python struct/hashlib vectors");
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
