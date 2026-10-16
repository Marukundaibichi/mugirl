using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// 读档前缀是存档升级的入口：此时整个存档 DOM 已在内存中，Scribe 还没有读取任何数据（curXmlParent 是 <game>）。
// Game.LoadGame 唯一的调用方是 SavedGameLoaderNow，所以这里只会遇到存档文件。前缀排在最前，任何读取存档的补丁都看到升级后的 DOM。
// 后缀是阶段 C；终结器只清除会话，不吞掉原方法的异常（void 终结器让 Harmony 重新抛出原异常）。
[HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.Critical, "Saves from older Mugirl versions load without the upgrade: renamed classes, defs and labels are lost with load errors.")]
internal static class Harmony_Game_LoadGame
{
    [HarmonyPriority(Priority.First)]
    internal static void Prefix() => SaveUpgradeRuntime.BeginLoad();

    internal static void Postfix() => SaveUpgradeRuntime.CompleteLoad();

    internal static void Finalizer() => SaveUpgradeRuntime.EndLoad();
}
