using HarmonyLib;
using RimWorld;

namespace MooGirl
{
    [HarmonyPatch(typeof(PawnGroupMaker), nameof(PawnGroupMaker.CanGenerateFrom))]
    public static class Harmony_GiantCorpRaidTiers
    {
        private const float LowMax = 2000f;
        private const float LowerMiddleMax = 4000f;
        private const float HighMax = 6500f;
        private const float TopMax = 10000f;

        public static void Postfix(PawnGroupMaker __instance, PawnGroupMakerParms parms, ref bool __result)
        {
            if (!__result || __instance == null || parms?.faction?.def != MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile)
            {
                return;
            }

            if (__instance.kindDef != PawnGroupKindDefOf.Combat || parms.groupKind != PawnGroupKindDefOf.Combat)
            {
                return;
            }

            float points = parms.points;
            float max = __instance.maxTotalPoints;

            if (max == LowMax)
            {
                __result = points >= 0f && points <= LowMax;
            }
            else if (max == LowerMiddleMax)
            {
                __result = points > LowMax && points <= LowerMiddleMax;
            }
            else if (max == HighMax)
            {
                __result = points > LowerMiddleMax && points <= HighMax;
            }
            else if (max == TopMax)
            {
                __result = points > HighMax && points <= TopMax;
            }
        }
    }
}
