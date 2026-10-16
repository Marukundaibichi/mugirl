using HarmonyLib;

namespace Mugirl.Features.Mounting;

// 骑乘 verb 的暖机姿态不画原版瞄准扇形（骑手不在地图上，扇形会画在载具身上）。
// 骑乘施放把 WarmupTime 覆盖为 0，正常不会产生 Stance_Warmup；保留它防御第三方 verb 自行设置暖机。
[HarmonyPatch(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceDraw))]
[MugirlPatch(PatchModule.Mounting, PatchRisk.High, "Mounted verbs use the base warmup stance draw path.")]
public static class Harmony_Stance_Warmup_StanceDraw_MountedVerbs
{
    public static bool Prefix(Stance_Warmup __instance)
    {
        return !MountedCombatController.IsMountedVerb(__instance.verb);
    }
}
