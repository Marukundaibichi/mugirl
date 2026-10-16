using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), [typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool)])]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Critical, "Weapon-wheel equip animation and cycle cooldown no longer gate firing.")]
public static class Harmony_Verb_TryStartCastOn_WeaponWheel
{
    // 抬枪动画、换枪动画与循环冷却期间拦下原版开火（跳过原方法并给出结果）；动画结束后由轮盘重新发起。
    [HarmonyPriority(Priority.Normal)]
    public static bool Prefix(
        Verb __instance,
        LocalTargetInfo castTarg,
        LocalTargetInfo destTarg,
        bool surpriseAttack,
        bool canHitNonTargetPawns,
        bool preventFriendlyFire,
        bool nonInterruptingSelfCast,
        ref bool __result)
    {
        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance.CasterPawn);
        if (comp == null || !comp.HandleBeforeTryStartCast(
            __instance,
            castTarg,
            destTarg,
            surpriseAttack,
            canHitNonTargetPawns,
            preventFriendlyFire,
            nonInterruptingSelfCast,
            out bool handledResult))
        {
            return true;
        }

        __result = handledResult;
        return false;
    }

    public static void Postfix(Verb __instance, bool __result)
    {
        WeaponWheelLookup.CompFor(__instance.CasterPawn)?.NotifyCastStarted(__instance, __result);
    }
}
