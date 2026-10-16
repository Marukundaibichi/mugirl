using HarmonyLib;

namespace Mugirl.Features.Incidents;

// 投资人头顶显示可交谈的问号。目标是没有覆写的小虚方法，可能被 JIT 内联：只能在 Mod 构造函数中打补丁（HarmonyBootstrap 默认如此）。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.ShouldShowQuestionMark))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Fusion investors may not show the question-mark interaction indicator.")]
internal static class Harmony_Pawn_ShouldShowQuestionMark_FusionInvestor
{
    public static void Postfix(Pawn __instance, ref bool __result)
    {
        if (!__result && FusionInvestmentUtility.CanTalkToInvestor(__instance))
        {
            __result = true;
        }
    }
}
