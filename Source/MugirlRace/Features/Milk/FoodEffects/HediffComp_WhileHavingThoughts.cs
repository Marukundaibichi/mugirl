namespace Mugirl.Features.Milk;

// 周期性检查 Pawn 记忆：指定 Thought 都不存在时移除自身，并可把另一些记忆的心情效果清零。
internal sealed class HediffComp_WhileHavingThoughts : HediffComp
{
    private const int CheckingIntervalTicks = 600;

    // 存档键沿用 1.6：mugirlThoughtActive、mugirlThoughtCheckTicks。初值 600 让新建的 Hediff 在下一次间隔 tick 就检查一次。
    public bool mugirlThoughtActive;

    public int mugirlThoughtCheckTicks = CheckingIntervalTicks;

    // 交给原版 HealthTickInterval 按 ShouldRemove 移除，不在 tick 中直接删除自身。
    private bool removeRequested;

    public HediffCompProperties_WhileHavingThoughts? Props => props as HediffCompProperties_WhileHavingThoughts;

    public override bool CompShouldRemove => base.CompShouldRemove || removeRequested;

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref mugirlThoughtActive, "mugirlThoughtActive", false);
        Scribe_Values.Look(ref mugirlThoughtCheckTicks, "mugirlThoughtCheckTicks", CheckingIntervalTicks);
    }

    // 创建时先处理一次可选的严重度削减，之后只负责 Thought 检查。
    public override void CompPostMake()
    {
        base.CompPostMake();

        HediffCompProperties_WhileHavingThoughts? thoughtProps = Props;
        HediffDef? reductionDef = thoughtProps?.hediffReduction;
        if (thoughtProps == null || reductionDef == null)
        {
            return;
        }

        Hediff? reduced = Pawn?.health?.hediffSet?.GetFirstHediffOfDef(reductionDef, false);
        if (reduced != null)
        {
            reduced.Severity -= thoughtProps.reductionAmount;
        }
    }

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        base.CompPostTickInterval(ref severityAdjustment, delta);

        HediffCompProperties_WhileHavingThoughts? thoughtProps = Props;
        if (thoughtProps == null)
        {
            removeRequested = true;
            return;
        }

        mugirlThoughtCheckTicks += delta;
        if (mugirlThoughtCheckTicks <= CheckingIntervalTicks)
        {
            return;
        }

        mugirlThoughtCheckTicks = 0;
        MemoryThoughtHandler? memories = Pawn?.needs?.mood?.thoughts?.memories;
        if (memories == null)
        {
            removeRequested = true;
            return;
        }

        mugirlThoughtActive = HasAnyMemory(memories, thoughtProps.thoughtDefs);
        SuppressMemories(memories, thoughtProps.removeThoughtDefs);
        if (!mugirlThoughtActive)
        {
            removeRequested = true;
        }
    }

    private static bool HasAnyMemory(MemoryThoughtHandler memories, List<ThoughtDef?>? thoughtDefs)
    {
        if (thoughtDefs == null)
        {
            return false;
        }

        for (int i = 0; i < thoughtDefs.Count; i++)
        {
            ThoughtDef? def = thoughtDefs[i];
            if (def != null && memories.GetFirstMemoryOfDef(def) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void SuppressMemories(MemoryThoughtHandler memories, List<ThoughtDef?>? thoughtDefs)
    {
        if (thoughtDefs == null)
        {
            return;
        }

        for (int i = 0; i < thoughtDefs.Count; i++)
        {
            ThoughtDef? def = thoughtDefs[i];
            Thought_Memory? memory = def == null ? null : memories.GetFirstMemoryOfDef(def);
            if (memory != null)
            {
                memory.moodPowerFactor = 0f;
            }
        }
    }
}
