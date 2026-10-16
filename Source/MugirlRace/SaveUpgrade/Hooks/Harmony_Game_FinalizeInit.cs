using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// 阶段 B：地图与 MapComponent.FinalizeInit 之后、GameComponent.FinalizeInit 之前。新游戏也调用 FinalizeInit，此时没有读档会话。
[HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy in-flight Mugirl jobs keep running with old toil indices; phase B upgrade steps do not run.")]
internal static class Harmony_Game_FinalizeInit
{
    internal static void Prefix() => SaveUpgradeRuntime.RunPhaseB();
}
