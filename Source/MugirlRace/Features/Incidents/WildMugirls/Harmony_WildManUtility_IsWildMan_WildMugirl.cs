using HarmonyLib;

namespace Mugirl.Features.Incidents;

// 让逃亡野生奴隶走原版野人入口，方便玩家用熟悉的交互收编。
// 目标是调用极多的小静态方法，容易被 JIT 内联：只能在 Mod 构造函数中打补丁（HarmonyBootstrap 默认如此），后缀保持最少的工作量。
[HarmonyPatch(typeof(WildManUtility), nameof(WildManUtility.IsWildMan))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Escape wild slaves are no longer exposed through the vanilla wild-man helper.")]
internal static class Harmony_WildManUtility_IsWildMan_WildMugirl
{
    public static void Postfix(Pawn p, ref bool __result)
    {
        if (__result)
        {
            return;
        }

        if (WildMugirlUtility.IsNonPlayerWildMugirl(p) && !p.IsSubhuman)
        {
            __result = true;
        }
    }
}
