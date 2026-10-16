using HarmonyLib;

namespace Mugirl.Features.Mounting;

// Verb.WarmupTime 是只读字段的小 getter，Verb_LaunchProjectile 以非虚调用 base，可能被 JIT 内联；
// 补丁必须在 Mod 构造函数中应用（HarmonyBootstrap.ApplyAtStartup），不能改为延迟补丁。
// 骑乘施放时由 MountedWarmupOverride 把暖机覆盖为 0；没有覆盖时只做一次引用比较。
[HarmonyPatch(typeof(Verb), nameof(Verb.WarmupTime), MethodType.Getter)]
[MugirlPatch(PatchModule.Mounting, PatchRisk.High, "Mounted combat warmup override is skipped.")]
public static class Harmony_Verb_WarmupTime_MountedCast
{
    [HarmonyPriority(Priority.Normal)]
    public static void Postfix(Verb __instance, ref float __result)
    {
        MountedCombatController.TryGetWarmupTimeOverride(__instance, ref __result);
    }
}
