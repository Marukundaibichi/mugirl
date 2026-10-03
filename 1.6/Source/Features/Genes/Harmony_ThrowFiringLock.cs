using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 投掷进行中（举起瞄准或物体仍在空中）禁止持物者开枪。
    // 菜单层让射击指令直接不可用并给出理由；Verb 层兜底拦截第三方战斗模组
    // 绕过菜单直接发起的射击。近战与能力施放不在本约束范围内。
    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetRangedAttackAction))]
    public static class Harmony_ThrowFiringLock_RangedAttackMenu
    {
        public static void Postfix(Pawn pawn, ref Action __result, ref string failStr)
        {
            if (__result == null || MugirlThrowUtility.FindThrowingController(pawn) == null)
            {
                return;
            }

            failStr = "Mugirl.Throw.BusyHolding".Translate(pawn.LabelShort, pawn).ToString();
            __result = null;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Harmony_ThrowFiringLock_TryStartCastOn
    {
        // 高于武器轮盘等默认优先级的前置补丁，确保拦截时不触发轮盘的换枪动画与延迟施放。
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!(__instance is Verb_LaunchProjectile)
                || __instance.CasterPawn == null
                || MugirlThrowUtility.FindThrowingController(__instance.CasterPawn) == null)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
