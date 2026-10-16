using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

// 轮盘战斗所依赖的 Verb 补丁。补丁类名决定 HarmonyBootstrap 的应用顺序（FullName 序），改名前复查同目标补丁
// （Verb.TryStartCastOn 另有 Harmony_Verb_TryStartCastOn_ThrowLock，Verb.WarmupTime 另有骑乘战斗补丁）。

[HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Critical, "Full-firepower chains do not advance after a burst, or linked sword-dance damage resolves before the pawn visually crosses its target.")]
public static class Harmony_Verb_TryCastNextBurstShot
{
    // 剑舞触发时推迟本次近战结算（跳过原方法），直到冲刺越过目标再由 SwordDanceDelayedStrike 补发。
    public static bool Prefix(Verb __instance)
    {
        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance.CasterPawn);
        return comp?.TryDelaySwordDanceStrikeUntilImpact(__instance) != true;
    }

    public static void Postfix(Verb __instance)
    {
        if (__instance.verbProps?.IsMeleeAttack == true)
        {
            CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance.CasterPawn);
            if (comp != null && !comp.IsSwordDanceStrikeAwaitingImpact(__instance))
            {
                comp.NotifyMeleeAttackCompleted(__instance);
            }
            return;
        }

        WeaponWheelDevLog.BurstShotPostfix(__instance);
        // 只在本 tick 打出 burst 最后一发后接续轮射；其他模组推迟最后一发时不触发。
        if (__instance.state != VerbState.Idle
            || !TickUtility.TryGetCurrentGameTick(out int currentTick)
            || __instance.LastShotTick != currentTick)
        {
            return;
        }
        WeaponWheelLookup.CompFor(__instance.CasterPawn)?.NotifyBurstCompleted(__instance);
    }
}
