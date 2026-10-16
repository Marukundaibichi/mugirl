using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// Game.LoadGame 拿不到存档文件名（1.7.4977：LoadGameFromSaveFileNow 只把 FilePathForSavedGame(fileName) 交给 Scribe.loader），
// 这里记下文件名，升级前据此备份原始存档。
[HarmonyPatch(typeof(SavedGameLoaderNow), nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.Medium, "Legacy saves are upgraded without a backup of the original file.")]
internal static class Harmony_SavedGameLoaderNow_LoadGameFromSaveFileNow
{
    internal static void Prefix(string fileName) => SaveUpgradeRuntime.CaptureFileName(fileName);
}
