using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// 阶段 A：引用解析与 PostLoadInit 之后、任何物体生成到地图之前。ScribeLoader 也读设置、思想体系等文件，只在读档会话中运行。
[HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.FinalizeLoading))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Post-load upgrade steps of phase A do not run; legacy saves keep their schema stamp and report a failed upgrade.")]
internal static class Harmony_ScribeLoader_FinalizeLoading
{
    internal static void Postfix() => SaveUpgradeRuntime.RunPhaseA();
}
