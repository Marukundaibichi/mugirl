using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Verb), nameof(Verb.WarmupTime), MethodType.Getter)]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Chained wheel weapons retain their ordinary aiming delay.")]
public static class Harmony_Verb_WarmupTime_WheelChain
{
    // 小型 getter，可能被调用方内联：补丁必须在 Mod 构造期应用（HarmonyBootstrap 已保证）。
    [HarmonyPriority(Priority.Normal - 1)]
    public static void Postfix(Verb __instance, ref float __result)
    {
        if (WeaponWheelWarmupScope.IsSkipping(__instance))
        {
            __result = 0f;
        }
    }
}
