using Mugirl.Features.Appearance;
using Mugirl.Features.Incidents;

namespace Mugirl.SaveUpgrade;

// 仅保存需要在原 MapComponent.PostLoadInit 时点完成的旧格式动作。
internal sealed class MigrationInitialization : IMigrationInitialization
{
    internal int firstUnscheduledIndex = -1;
    internal bool releaseBikiniLocks;

    public MigrationInitialization()
    {
    }

    public void Complete(MigrationState state)
    {
        int currentTick = TickUtility.CurrentGameTickOrFallback(0);
        FillMissingSchedules(state, currentTick, static () => Rand.RangeInclusive(30, 300));

        if (releaseBikiniLocks)
        {
            ReleaseBikiniLocks();
            state.outcomeFailed = true;
        }
    }

    // 每次成功写入后推进持久游标。后续清理抛出时，再保存/加载不会重复消费已经完成的随机抽取。
    internal void FillMissingSchedules(MigrationState state, int currentTick, Func<int> drawDelay)
    {
        while (firstUnscheduledIndex >= 0 && firstUnscheduledIndex < state.members.Count)
        {
            int nextTick = currentTick + drawDelay();
            state.members[firstUnscheduledIndex].grazeAtTick = nextTick;
            firstUnscheduledIndex++;
        }

        firstUnscheduledIndex = -1;
    }

    private static void ReleaseBikiniLocks()
    {
        foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
        {
            if (!EventPawnTags.IsMigrationPawn(pawn) || EventPawnTags.IsRunawayFarmPawn(pawn) || pawn.apparel == null)
            {
                continue;
            }

            ThingDef bikiniDef = AppearanceDefOf.Mugirl_Bikini;
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def == bikiniDef)
                {
                    pawn.apparel.Unlock(apparel);
                }
            }
        }
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref firstUnscheduledIndex, "firstUnscheduledIndex", -1);
        Scribe_Values.Look(ref releaseBikiniLocks, "releaseBikiniLocks", false);
    }
}
