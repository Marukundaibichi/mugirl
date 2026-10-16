using HarmonyLib;

namespace Mugirl.SaveUpgrade;

// DOM 只清理类型确定位置中的已删除 Def；其余由 Scribe_Defs 读取的残留引用（记忆、来源、排队工作等）读为 null，
// 原版用 WasDefRemoved 决定是否记录“Could not load reference”。删除行（含隐含 Def）在这里返回 true。
// 规则见 DefNameResolver.WasDropped；不受读档会话门控：异种、思想体系、剧本文件同样会引用它们。
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.WasDefRemoved))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.Low, "Leftover references to removed Mugirl defs log a 'Could not load reference' error per occurrence while loading.")]
internal static class Harmony_BackCompatibility_WasDefRemoved
{
    internal static void Postfix(string defName, Type type, ref bool __result)
    {
        if (__result || !UpgradeTables.TryGetDefault(out UpgradeTables? tables, out _))
        {
            return;
        }

        if (DefNameResolver.WasDropped(tables, type, defName, static (root, name) => GenDefDatabase.GetDefSilentFail(root, name, specialCaseForSoundDefs: false) is not null))
        {
            __result = true;
        }
    }
}
