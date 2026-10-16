using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Corporate;

// 巨企支援小队的“死战条款”：特性本体只负责展示与判定，
// 个人恐慌与友伤折扣由本文件补丁处理，集体减员撤退由支援 LordJob 禁用。
internal static class CorporateDiehardUtility
{
    // 原版 Faction.Notify_MemberTookDamage 的友伤惩罚为 -1.3×伤害(上限100)；
    // 死士条款下仅保留 5%，即关系损失降低 95%。
    public const float FriendlyFireGoodwillFactor = 0.05f;

    public static bool IsDiehard([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null && pawn.story?.traits != null
            && pawn.story.traits.HasTrait(CorporateDefOf.Mugirl_CorporateDiehard);
    }

    public static void MakeDiehard(Pawn? pawn)
    {
        if (pawn?.story?.traits == null)
        {
            return;
        }

        if (!IsDiehard(pawn))
        {
            pawn.story.traits.GainTrait(new Trait(CorporateDefOf.Mugirl_CorporateDiehard));
        }

        // 原生双保险：两者都关闭受击触发的个体恐慌逃亡（PanicFlee）。
        pawn.mindState.canFleeIndividual = false;
        pawn.mindState.mentalStateHandler.neverFleeIndividual = true;
    }

    /// <summary>死士成员友伤好感惩罚的缩减值：按 Unity Mathf.RoundToInt（银行家舍入）保持 1.6 的取整结果。</summary>
    internal static int ScaleFriendlyFirePenalty(int goodwillChange)
        => UnityMathf.RoundToInt(goodwillChange * FriendlyFireGoodwillFactor);
}
