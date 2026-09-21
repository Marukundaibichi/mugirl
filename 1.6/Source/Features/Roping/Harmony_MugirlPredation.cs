using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    public static class Patch_FoodUtility_IsAcceptablePreyFor_Mugirl
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn predator, Pawn prey, ref bool __result)
        {
            if (__result &&
                MugirlIdentity.IsMugirlPawn(predator) &&
                prey?.RaceProps?.Humanlike == true)
            {
                __result = false;
            }
        }
    }
}
