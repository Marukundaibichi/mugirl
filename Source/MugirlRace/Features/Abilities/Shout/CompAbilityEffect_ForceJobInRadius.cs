using Verse.AI;

namespace Mugirl.Features.Abilities;

// 对施法者周围的敌人强行下达指定 Job，用于嘲讽等控制类能力。
public class CompAbilityEffect_ForceJobInRadius : CompAbilityEffect
{
    private new CompProperties_AbilityEffect_ForceJobInRadius? Props => props as CompProperties_AbilityEffect_ForceJobInRadius;

    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        Pawn? caster = parent?.pawn;
        Map? map = caster?.Map;
        CompProperties_AbilityEffect_ForceJobInRadius? forceProps = Props;
        if (caster is null || map?.mapPawns is null || forceProps?.jobDef is not { } jobDef || forceProps.radius < 0f)
        {
            return;
        }

        float radiusSquared = forceProps.radius * forceProps.radius;

        // 以施法者为中心扫描敌人，半径判断使用平方距离避免重复开方。下达 Job 不会增删地图上的 Pawn，可直接按索引遍历。
        IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn p = pawns[i];
            if (CanForceJobOn(p, caster, map, radiusSquared))
            {
                StartForcedJob(p, caster, jobDef, forceProps.durationTick);
            }
        }

        base.Apply(target, dest);
    }

    private static bool CanForceJobOn(Pawn? target, Pawn caster, Map map, float radiusSquared)
    {
        return target is not null
            && target != caster
            && target.Spawned
            && target.Map == map
            && target.jobs is not null
            && !target.Dead
            && !target.Downed
            && target.HostileTo(caster)
            && target.Position.DistanceToSquared(caster.Position) <= radiusSquared;
    }

    private static void StartForcedJob(Pawn target, Pawn caster, JobDef jobDef, int durationTicks)
    {
        Job job = JobMaker.MakeJob(jobDef, caster);
        if (durationTicks > 0)
        {
            // XML 中的 durationTick 表示强制任务最多维持多久；不设置时沿用 JobDef 自身结束条件。
            job.expiryInterval = durationTicks;
            job.checkOverrideOnExpire = true;
        }

        target.jobs.StopAll(false, true);
        target.jobs.StartJob(job, JobCondition.InterruptForced);

        // 强制任务之外还要指定敌人目标，否则部分 AI 会在下一次决策时转火。
        if (target.mindState is not null)
        {
            target.mindState.enemyTarget = caster;
        }
    }

    // AI 只在目标正瞄准自己时使用，避免该能力变成无条件群控。
    public override bool AICanTargetNow(LocalTargetInfo target)
    {
        Pawn? caster = parent?.pawn;
        if (caster is null || PlayerFactionUtility.IsPlayerFaction(caster.Faction))
        {
            return false;
        }

        if (target.Thing is Pawn targetPawn && !targetPawn.Dead && !targetPawn.Downed)
        {
            return targetPawn.TargetCurrentlyAimingAt == caster;
        }

        return false;
    }
}
