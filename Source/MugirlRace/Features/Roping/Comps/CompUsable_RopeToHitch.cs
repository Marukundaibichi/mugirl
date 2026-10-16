using Verse.AI;

namespace Mugirl.Features.Roping;

// 墙绳桩与商队打包点上的“系到建筑”菜单（Building_Rope.xml、Patches/MugirlPatch.xml 按类名引用）。
// 建筑被移除时解开系在其格子上的雪牛娘。
public class CompUsable_RopeToHitch : CompUsable
{
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        RopingService.NotifyWallRopeHitchRemoved(map, parent.PositionHeld);
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        RopingService.NotifyWallRopeHitchRemoved(previousMap, parent.PositionHeld);
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (!pawn.CanReserve(parent))
        {
            yield return new FloatMenuOption("Reserved".Translate(), null, MenuOptionPriority.DisabledOption);
            yield break;
        }

        if (!pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield break;
        }

        if (RopingService.FirstMugirlFollowing(pawn) is not null)
        {
            yield return new FloatMenuOption("Mugirl.RopeToHitch".Translate(), () => StartRoping(pawn));
        }
        else
        {
            yield return new FloatMenuOption("Mugirl.NoValidToRope".Translate(), null, MenuOptionPriority.DisabledOption);
        }
    }

    private void StartRoping(Pawn pawn)
    {
        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some)
            || RopingService.FirstMugirlFollowing(pawn) is not { } ropee)
        {
            return;
        }

        Job job = JobMaker.MakeJob(RopingDefOf.Mugirl_RopeToHitch, parent);
        job.targetB = ropee;
        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }
}
