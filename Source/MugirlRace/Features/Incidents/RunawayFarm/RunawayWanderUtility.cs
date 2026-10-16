using Verse.AI;

namespace Mugirl.Features.Incidents;

internal static class RunawayWanderUtility
{
    private static readonly IntRange WanderExpiryTicks = new IntRange(420, 900);

    // 逃跑的雪牛娘被强制四处游荡。1.6 用 TryTakeOrderedJob 下达，它在 tick 中读取“排队下令”快捷键与 Event 状态
    // （1.7 反编译核对），按住 Shift 时会不断把新工作排进队列；这里直接开始工作，保留原来的 playerForced 标记。
    // 当前工作不可被玩家打断（倒地、着火等）时不下令，由调用方稍后重试。
    internal static bool TryForceWander(Pawn pawn, IntVec3 around, int radius)
    {
        Map? map = pawn.Map;
        Pawn_JobTracker? jobs = pawn.jobs;
        if (map == null || jobs == null || !jobs.IsCurrentJobPlayerInterruptible()
            || pawn.CurJob?.def.forceCompleteBeforeNextJob == true)
        {
            return false;
        }

        Job job = JobMaker.MakeJob(JobDefOf.GotoWander, CellFinder.RandomClosewalkCellNear(around, map, radius));
        job.expiryInterval = WanderExpiryTicks.RandomInRange;
        job.playerForced = true;
        jobs.ClearQueuedJobs();
        jobs.StartJob(job, JobCondition.InterruptForced, tag: JobTag.Misc);
        return true;
    }
}
