using HarmonyLib;

namespace Mugirl.Features.Abilities;

// 开火锁的 Verb 层：征召自动开火与绕过射击菜单的第三方战斗模组都经由 TryStartCastOn 发起射击。
[HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), [typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool)])]
[MugirlPatch(PatchModule.Abilities, PatchRisk.Medium, "投掷进行中的持物者可被绕过射击菜单的第三方战斗模组直接发起开火。")]
public static class Harmony_Verb_TryStartCastOn_ThrowLock
{
    // 高于武器轮盘等默认优先级的前置补丁，确保拦截时不触发轮盘的换枪动画与延迟施放。
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Verb __instance, ref bool __result)
    {
        if (!IsShootingVerb(__instance)
            || __instance.CasterPawn is not { } caster
            || ThrowUtility.FindThrowingController(caster) is null)
        {
            return true;
        }

        __result = false;
        return false;
    }

    // 弹射类 Verb（含能力射击）之外，装备武器的其他远程 Verb（光束、喷射、一次性武器）同样算开枪，与菜单层一致；
    // 近战、能力施放与服装 Verb（跳跃背包、烟雾弹等）不受约束。
    private static bool IsShootingVerb(Verb verb)
    {
        return verb is Verb_LaunchProjectile
            || (!verb.IsMeleeAttack && verb.EquipmentCompSource is not null);
    }
}
