using HarmonyLib;

namespace Mugirl.Features.Abilities;

// 投掷进行中（举起瞄准或物体仍在空中）禁止持物者开枪。
// 菜单层让射击指令直接不可用并给出理由；Verb 层（Harmony_Verb_TryStartCastOn_ThrowLock）兜底拦截绕过菜单的开火。
[HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetRangedAttackAction))]
[MugirlPatch(PatchModule.Abilities, PatchRisk.Medium, "投掷进行中的持物者可再次通过射击菜单下达开火指令，举起或飞行中的物体会照常被射击打断。")]
public static class Harmony_FloatMenuUtility_GetRangedAttackAction_ThrowLock
{
    public static void Postfix(Pawn pawn, ref Action? __result, ref string? failStr)
    {
        if (__result is null || ThrowUtility.FindThrowingController(pawn) is null)
        {
            return;
        }

        failStr = "Mugirl.Throw.BusyHolding".Translate(pawn.LabelShort, pawn).ToString();
        __result = null;
    }
}
