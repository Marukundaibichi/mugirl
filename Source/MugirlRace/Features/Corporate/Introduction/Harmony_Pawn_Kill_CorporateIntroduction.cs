using HarmonyLib;

namespace Mugirl.Features.Corporate;

// PatchGovernance: 只在原版实际死亡后保存送货员结果，不把开始交战误认为死亡。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Courier death details fall back to neutral historical dialogue.")]
internal static class Harmony_Pawn_Kill_CorporateIntroduction
{
    private static void Postfix(Pawn __instance, DamageInfo? dinfo)
    {
        if (__instance.Dead)
        {
            CorporateIntroduction.Current?.NotifyKilled(__instance, dinfo);
        }
    }
}
