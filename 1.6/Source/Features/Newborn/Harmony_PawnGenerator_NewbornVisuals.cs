using HarmonyLib;
using System;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(PawnGenerator))]
    [HarmonyPatch("GeneratePawn")]
    [HarmonyPatch(new Type[] { typeof(PawnGenerationRequest) })]
    public static class Harmony_PawnGenerator_NewbornVisuals
    {
        public static void Postfix(Pawn __result)
        {
            if (__result == null) return;

            if (MooGirlIdentity.HasMooGirlBody(__result) &&
                __result.ageTracker?.CurLifeStage != null &&
                __result.story != null)
            {
                LifeStageVisualService.NormalizeBodyType(__result);
            }
        }
    }
}
