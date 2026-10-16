using System.Linq;
using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 巨企员工（运货员、代表、支援队）按 PawnKindDef.apparelRequired 补齐制服，即使意识形态压制服装生成。
internal static class CorporateUniforms
{
    internal static void EnsureRequiredApparel(Pawn? pawn)
    {
        if (pawn?.apparel == null || pawn.kindDef.apparelRequired == null)
        {
            return;
        }

        foreach (ThingDef def in pawn.kindDef.apparelRequired)
        {
            if (pawn.apparel.WornApparel.Any(a => a.def == def))
            {
                continue;
            }

            var apparel = (Apparel)CorporateNetwork.MakeProduct(def, null, pawn.kindDef.itemQuality);
            apparel.SetColor(pawn.kindDef.apparelColor);
            // 只作用于新生成的员工，包括会压制服装的意识形态。
            foreach (Apparel old in pawn.apparel.WornApparel.Where(a => !ApparelUtility.CanWearTogether(a.def, def, pawn.RaceProps.body)).ToList())
            {
                old.Destroy();
            }

            pawn.apparel.Wear(apparel, false);
        }
    }

    internal static bool IsCorporateEmployee(PawnKindDef? kind)
    {
        return kind != null && (kind == CorporateDefOf.Mugirl_GiantCorp_Courier
            || kind == CorporateDefOf.Mugirl_CorporateRepresentative
            || kind == CorporateDefOf.Mugirl_CorporateSupport);
    }
}
