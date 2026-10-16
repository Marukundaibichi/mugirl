namespace Mugirl.Features.Social;

internal static class PregnancyMoodUtility
{
    internal static void EnsurePositiveThought(HediffComp_GiveRandomSituationalThought? comp)
    {
        if (!ModsConfig.BiotechActive
            || comp?.parent?.pawn is not { } pawn
            || comp.parent.def != HediffDefOf.PregnancyMood
            || !MugirlIdentity.IsMugirlPawn(pawn)
            || IsPositive(comp.selectedThought))
        {
            return;
        }

        // 沿用 Def 的正面心情及数值，不修改共享候选列表或其他种族的抽取结果。
        List<ThoughtDef>? thoughts = comp.Props?.thoughtDefs;
        if (thoughts is null)
        {
            return;
        }

        // 蓄水池抽样：在全部正面候选中等概率选一个，随机调用次数与 1.6 相同。
        ThoughtDef? replacement = null;
        int positiveCount = 0;
        for (int i = 0; i < thoughts.Count; i++)
        {
            if (IsPositive(thoughts[i]))
            {
                positiveCount++;
                if (Rand.RangeInclusive(1, positiveCount) == 1)
                {
                    replacement = thoughts[i];
                }
            }
        }

        // 其他 mod 移除全部正面候选时保留原状态，避免生成无效 Thought。
        if (replacement is not null)
        {
            comp.selectedThought = replacement;
        }
    }

    private static bool IsPositive(ThoughtDef? thought)
    {
        return thought?.stages is { Count: > 0 } stages
            && stages[0].baseMoodEffect > 0f;
    }
}
