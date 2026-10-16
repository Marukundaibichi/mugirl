using HarmonyLib;

namespace Mugirl.Features.Grazing;

// 野生或饥饿的雪牛娘不把类人 Pawn 当作活体猎物。规则属于种族行为，历史上放在牵引模块中。
[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.Medium, "Wild or starving Mugirls can again select humanlike pawns as live prey.")]
public static class Harmony_FoodUtility_IsAcceptablePreyFor
{
    [HarmonyPostfix]
    public static void Postfix(Pawn predator, Pawn? prey, ref bool __result)
    {
        if (__result && MugirlIdentity.IsMugirlPawn(predator) && prey?.RaceProps?.Humanlike == true)
        {
            __result = false;
        }
    }
}
