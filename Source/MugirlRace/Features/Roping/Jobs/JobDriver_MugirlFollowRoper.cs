using Verse.AI;

namespace Mugirl.Features.Roping;

// 雪牛娘跟随牵引者（JobDefs.xml Job_FollowRoper；类名作为 curDriver 写入存档）。
// 1.6 版逐行复制了原版 Verse.AI.JobDriver_FollowRoper，只多了下面的牵引关系检查；1.7 改为继承原版，
// 跟随、行进队形与站位逻辑随原版更新。
public class JobDriver_MugirlFollowRoper : Verse.AI.JobDriver_FollowRoper
{
    protected override IEnumerable<Toil> MakeNewToils()
    {
        // 兼容旧存档及异常中断留下的孤儿跟随 Job：跟随不能脱离 RopeTracker 的实际牵引关系独立存在。
        this.FailOn(() => pawn.roping?.RopedByPawn != job.GetTarget(TargetIndex.A).Thing);
        return base.MakeNewToils();
    }
}
