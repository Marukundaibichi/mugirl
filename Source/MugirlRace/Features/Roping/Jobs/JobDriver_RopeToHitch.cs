using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Roping;

// 牵引者把正在跟随的雪牛娘系到墙绳桩或商队打包点（JobDefs.xml RopeToBuild；类名作为 curDriver 写入存档）。
// TargetA = 绳桩建筑，TargetB = 被系的雪牛娘。
public class JobDriver_RopeToHitch : JobDriver
{
    private const int FinishWaitTicks = 60;

    private Map? pendingSpotRopeMap;

    protected Building? WallRopeHitch => job.GetTarget(TargetIndex.A).Thing as Building;
    protected Pawn? RopeePawn => job.GetTarget(TargetIndex.B).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Building? hitch = WallRopeHitch;
        Pawn? ropee = RopeePawn;
        return CanStartRopeToHitch(pawn, ropee, hitch)
            && pawn.Reserve(hitch, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(ropee, job, 1, -1, null, errorOnFailed);
    }

    // 读档后 MakeNewToils 会重新执行；存档若发生在最后的等待阶段，ropee 已经系好、不再跟随。
    // 所以必须无条件生成全部 Toil，开始条件只在第一个 Toil 开始时检查（1.6 在这里 yield break，读档后跳过收尾等待）。
    // 两个瞬时 Toil 在同一 tick 内连续执行，待系状态不会跨 tick 存在，因此不写入存档。
    protected override IEnumerable<Toil> MakeNewToils()
    {
        Pawn? ropee = RopeePawn;
        Building? hitch = WallRopeHitch;
        AddFinishAction(_ => ClearPendingSpotRope(ropee));

        Toil gotoHitch = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A)
            .FailOn(() => !CanPlaceAtHitch(pawn, ropee, hitch) || !pawn.CanReach(hitch, PathEndMode.Touch, Danger.Deadly));
        gotoHitch.AddPreInitAction(() =>
        {
            if (!CanStartRopeToHitch(pawn, ropee, hitch))
            {
                EndJobWith(JobCondition.Incompletable);
            }
        });
        yield return gotoHitch;

        yield return Toils_General.Do(() =>
        {
            if (!CanStartRopeToHitch(pawn, ropee, hitch))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (ropee.roping is not null)
            {
                RopingService.DropPawnRopeAndNotify(RopingService.RoperFor(ropee), ropee);
                RopingService.BreakAllRopesAndNotify(ropee);
                if (ropee.jobs is not null)
                {
                    ropee.jobs.ClearQueuedJobs();
                    ropee.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            pendingSpotRopeMap = ropee.Map;
            RopingService.MarkPendingSpotRope(ropee);
        });

        yield return Toils_General.Do(() =>
        {
            if (!CanPlaceAtHitch(pawn, ropee, hitch))
            {
                ClearPendingSpotRope(ropee);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            ropee.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(ropee);
            // 瞬移到绳桩格：同步重置寻路与绘制插值，否则 Pawn 会沿旧路径继续或从原位置滑过来。
            ropee.Position = hitch.Position;
            ropee.Notify_Teleported(endCurrentJob: false, resetTweenedPos: true);
            ropee.roping ??= new Pawn_RopeTracker(ropee);
            ropee.roping.RopeToSpot(hitch.Position);
            ClearPendingSpotRope(ropee);
        });

        yield return Toils_General.Wait(FinishWaitTicks);
    }

    private static bool CanStartRopeToHitch([NotNullWhen(true)] Pawn? roper, [NotNullWhen(true)] Pawn? ropee, [NotNullWhen(true)] Building? hitch)
    {
        return CanPlaceAtHitch(roper, ropee, hitch)
            && RopingService.IsMugirlRopee(ropee)
            && RopingService.IsFollowingRoper(ropee)
            && ropee.CurJob?.targetA.Thing == roper;
    }

    private static bool CanPlaceAtHitch([NotNullWhen(true)] Pawn? roper, [NotNullWhen(true)] Pawn? ropee, [NotNullWhen(true)] Building? hitch)
    {
        return roper is not null
            && ropee is not null
            && hitch is not null
            && roper.Spawned
            && ropee.Spawned
            && hitch.Spawned
            && !roper.Dead
            && !ropee.Dead
            && !roper.Destroyed
            && !ropee.Destroyed
            && !hitch.Destroyed
            && roper.Map == hitch.Map
            && ropee.Map == hitch.Map;
    }

    private void ClearPendingSpotRope(Pawn? ropee)
    {
        RopingService.ClearPendingSpotRope(ropee, pendingSpotRopeMap);
        pendingSpotRopeMap = null;
    }
}
