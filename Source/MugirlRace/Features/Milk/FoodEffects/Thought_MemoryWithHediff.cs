namespace Mugirl.Features.Milk;

// Consumed_MugirlMilk 的 thoughtClass：记忆生效时附带 ThoughtDef.hediff（雪牛奶的疗愈 Hediff），合并记忆时刷新它。
internal sealed class Thought_MemoryWithHediff : Thought_Memory
{
    // 一次性副作用标记：Hediff 在第一次执行 MoodOffset 时附加，读档后不再重复附加。
    public bool mugirlMemoryHediffAdded;

    // 1.6 漏掉了 base.ExposeData()，记忆的 def、age 等不进存档，读档后整条记忆被原版丢弃，
    // 依赖它的疗愈 Hediff（HediffComp_WhileHavingThoughts）随之在 600 tick 内消失。
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref mugirlMemoryHediffAdded, "mugirlMemoryHediffAdded", false);
    }

    public override float MoodOffset()
    {
        if (!mugirlMemoryHediffAdded)
        {
            if (def.hediff != null && !ThoughtUtility.ThoughtNullified(pawn, def))
            {
                FoodEffectUtility.AddOrRefreshHediff(pawn, def.hediff);
            }

            mugirlMemoryHediffAdded = true;
        }

        return base.MoodOffset();
    }

    public override bool TryMergeWithExistingMemory(out bool showBubble)
    {
        bool merged = base.TryMergeWithExistingMemory(out showBubble);
        if (merged && def.hediff != null && !ThoughtUtility.ThoughtNullified(pawn, def))
        {
            FoodEffectUtility.AddOrRefreshHediff(pawn, def.hediff);
        }

        return merged;
    }
}
