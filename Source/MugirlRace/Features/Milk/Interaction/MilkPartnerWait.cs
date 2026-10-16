using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Milk;

// 互动期间让另一方保持等待姿态的 Job 锁。发起方记录等待 Job 的 loadID，结束时只中断自己创建的那份等待，
// 不误伤对方之后接到的其他 Job。loadID 随发起方的 JobDriver 存档（键 ScribeLabel），读档后仍能正确解锁。
internal static class MilkPartnerWait
{
    internal const int NoJob = -1;

    internal const string ScribeLabel = "forcedWaitJobLoadId";

    // 以 InterruptForced 让 partner 立刻开始 waitJob（原 Job 之后恢复），返回其 loadID。
    internal static int Begin(Pawn partner, Job waitJob)
    {
        partner.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
        return CurrentLoadId(partner);
    }

    internal static int CurrentLoadId(Pawn partner) => partner.CurJob?.loadID ?? NoJob;

    // partner 仍在执行 loadID 相同的 waitDef 等待 Job 时才结束它。
    internal static void End(Pawn? partner, JobDef waitDef, int loadId)
    {
        if (IsWaiting(partner, waitDef, loadId))
        {
            partner.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
        }
    }

    internal static bool IsWaiting([NotNullWhen(true)] Pawn? partner, JobDef waitDef, int loadId)
    {
        return loadId != NoJob
            && partner != null
            && !partner.Destroyed
            && partner.CurJobDef == waitDef
            && partner.CurJob?.loadID == loadId;
    }

    internal static void Expose(ref int loadId) => Scribe_Values.Look(ref loadId, ScribeLabel, NoJob);
}
