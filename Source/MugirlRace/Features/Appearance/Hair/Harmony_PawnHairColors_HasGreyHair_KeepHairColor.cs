using HarmonyLib;

namespace Mugirl.Features.Appearance;

[HarmonyPatch(typeof(PawnHairColors), nameof(PawnHairColors.HasGreyHair))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Low, "Newly generated older Mugirls can receive HAR's muted old-hair color instead of keeping their original hair color.")]
internal static class Harmony_PawnHairColors_HasGreyHair_KeepHairColor
{
    internal static void Postfix(Pawn? pawn, ref bool __result)
    {
        // HAR 会沿用此结果选择种族的老年发色；雪牛娘保留生成时原本的发色。
        if (__result && MugirlIdentity.IsMugirlPawn(pawn))
        {
            __result = false;
        }
    }
}
