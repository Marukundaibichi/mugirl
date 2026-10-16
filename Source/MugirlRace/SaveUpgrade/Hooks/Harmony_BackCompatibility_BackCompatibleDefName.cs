using HarmonyLib;
using System.Xml;

namespace Mugirl.SaveUpgrade;

// 所有 Scribe_Defs 引用、玩家的异种/思想体系/剧本文件和第三方 DefInjected 键都经过 BackCompatibleDefName；旧 defName 只在这里改名。
// 排在最后，按原始 defName 参数查表，只在当前结果找不到 Def 时覆盖（规则见 DefNameResolver）。不受读档会话门控。
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "References to renamed Mugirl defs in saves, xenotype/ideo/scenario files and third-party translations fail to load.")]
internal static class Harmony_BackCompatibility_BackCompatibleDefName
{
    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(Type defType, string defName, ref string __result, XmlNode? node = null)
    {
        if (!UpgradeTables.TryGetDefault(out UpgradeTables? tables, out _))
        {
            return;
        }

        if (DefNameResolver.Resolve(tables, defType, defName, __result, static (root, name) => GenDefDatabase.GetDefSilentFail(root, name, specialCaseForSoundDefs: false) is not null) is { } renamed)
        {
            if (SaveUpgradeRuntime.ActiveSession is { ReadTrace: { } trace, Document: { } document }
                && ReferenceEquals(node?.OwnerDocument, document.Xml))
            {
                trace.Scalar("DefReference", node, defName, renamed);
            }
            __result = renamed;
        }
    }
}
