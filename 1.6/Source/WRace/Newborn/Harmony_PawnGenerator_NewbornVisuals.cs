using HarmonyLib;
using RimWorld;
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

            // 仅对 MooGirl 类型新生儿生效
            if (__result.RaceProps.body == MooGirl_DefOf.MooGirlBody &&
                __result.ageTracker.AgeChronologicalYearsFloat == 0f)
            {
                __result.story.Childhood = MooGirl_DefOf.MooGirl_Newborn;
                __result.story.Adulthood = null;
            }

            LifeStageVisualService.NormalizeBodyType(__result);
        }
    }
}
