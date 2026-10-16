using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// 新游戏开始生成之前通知生命周期，上一局的进程级状态不会泄漏进新游戏。
[HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.Medium, "Per-game state registered for BeforeNewGame is not reset before a new game is generated.")]
internal static class Harmony_Game_InitNewGame
{
    internal static void Prefix() => GameLifecycle.Raise(GameLifecycleEvent.BeforeNewGame);
}
