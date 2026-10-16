using HarmonyLib;

namespace Mugirl.Features.Milk;

// 受雪牛娘哺育的 Pawn 每日满速经验额度放宽到原版的 2.5 倍（4000 + 6000）。
// 原版在 Learn → LearnRateFactor 中以 xpSinceMidnight > 4000 判定饱和。在放宽区间内，Prefix 把 xpSinceMidnight 暂时置 0，
// Learn 只会在其上累加本次经验；Finalizer 再加回原值，结果与原版的 xpSinceMidnight += xp 逐位相同，异常时也会恢复。
// 1.6 的写法每次把 xpSinceMidnight 压回 4000 并写入存档，受哺育者因此永远不会饱和。
[HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
[MugirlPatch(PatchModule.Milk, PatchRisk.High, "The nurtured trait no longer relaxes the daily full-rate XP cap.")]
public static class Harmony_SkillRecord_Learn_NurturedXpCap
{
    // 最后执行的 Prefix：其他 Prefix 看到的仍是原值。
    [HarmonyPriority(Priority.Last)]
    public static void Prefix(SkillRecord __instance, float xp, bool direct, bool ignoreLearnRate, out float __state)
    {
        __state = 0f;
        if (direct || ignoreLearnRate || xp <= 0f || !NurtureMath.ShouldBypassSaturation(__instance.xpSinceMidnight))
        {
            return;
        }

        TraitSet? traits = __instance.Pawn?.story?.traits;
        if (traits == null || !traits.HasTrait(MilkDefOf.Mugirl_NurturedByMugirl))
        {
            return;
        }

        __state = __instance.xpSinceMidnight;
        __instance.xpSinceMidnight = 0f;
    }

    // 最先执行的 Finalizer：在其他补丁读取前恢复。__state 为 0 表示 Prefix 未改动（放宽只发生在原值大于 4000 时）。
    [HarmonyPriority(Priority.First)]
    public static void Finalizer(SkillRecord __instance, float __state)
    {
        if (__state > 0f)
        {
            __instance.xpSinceMidnight = __state + __instance.xpSinceMidnight;
        }
    }
}
