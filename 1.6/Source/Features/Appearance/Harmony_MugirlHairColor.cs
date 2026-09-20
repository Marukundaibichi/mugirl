using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(PawnHairColors), nameof(PawnHairColors.HasGreyHair))]
    internal static class Harmony_PawnHairColors_HasGreyHair_Mugirl
    {
        internal static void Postfix(Pawn pawn, ref bool __result)
        {
            // HAR 会沿用此结果选择种族的老年发色；雪牛娘保留生成时原本的发色。
            if (__result && MugirlIdentity.IsMugirlPawn(pawn))
            {
                __result = false;
            }
        }
    }
}
