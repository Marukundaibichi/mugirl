using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class CorporateUniforms
    {
        internal static void EnsureRequiredApparel(Pawn pawn)
        {
            if (pawn?.apparel == null || pawn.kindDef.apparelRequired == null) return;
            foreach (ThingDef def in pawn.kindDef.apparelRequired)
            {
                if (pawn.apparel.WornApparel.Any(a => a.def == def)) continue;
                var apparel = (Apparel)CorporateNetwork.MakeProduct(def, null, pawn.kindDef.itemQuality);
                apparel.SetColor(pawn.kindDef.apparelColor);
                // Restricted to newly generated employees, including ideologies that suppress clothing.
                foreach (Apparel old in pawn.apparel.WornApparel.Where(a => !ApparelUtility.CanWearTogether(a.def, def, pawn.RaceProps.body)).ToList())
                    old.Destroy();
                pawn.apparel.Wear(apparel, false);
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    internal static class Harmony_CorporateUniforms
    {
        private static void Postfix(Pawn __result)
        {
            string kind = __result?.kindDef?.defName;
            if (kind == "AI_GC_Courier" || kind == "Mugirl_CorporateRepresentative" || kind == "Mugirl_CorporateSupport")
                CorporateUniforms.EnsureRequiredApparel(__result);
        }
    }
}
