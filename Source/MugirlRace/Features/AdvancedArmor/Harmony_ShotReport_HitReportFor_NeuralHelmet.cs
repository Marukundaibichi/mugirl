using HarmonyLib;

namespace Mugirl.Features.AdvancedArmor;

// 神经增幅头盔：射击命中率不受明暗（Ideology 的明暗射击修正）影响，私有字段 ShotReport.offsetFromDarkness 置 0。
// 与 1.6 相同，正向的明暗加成也一并清零。
[HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
[MugirlPatch(PatchModule.AdvancedArmor, PatchRisk.Medium, "The neural amplifier helmet no longer cancels darkness accuracy offsets.")]
internal static class Harmony_ShotReport_HitReportFor_NeuralHelmet
{
    // StaticCacheLifecycle: 进程级的私有字段访问器，打补丁时在 Prepare 中解析一次，不持有游戏对象。
    // 1.6 用手写 DynamicMethod 发出 stfld；这里改用 Harmony 提供的结构体字段引用访问器。
    private static AccessTools.StructFieldRef<ShotReport, float>? darknessOffset;

    // 字段不存在时抛出异常，由 HarmonyBootstrap 记录并只停用本补丁。
    private static bool Prepare()
    {
        darknessOffset ??= AccessTools.StructFieldRefAccess<ShotReport, float>("offsetFromDarkness");
        return true;
    }

    private static void Postfix(Thing? caster, ref ShotReport __result)
    {
        if (darknessOffset is null || caster is not Pawn pawn || !AdvancedArmorUtility.WearsNeuralHelmet(pawn))
        {
            return;
        }

        darknessOffset(ref __result) = 0f;
    }
}
