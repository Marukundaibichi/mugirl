using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 强制任务能力的 XML 配置：指定影响半径、目标 Job 和可选持续时间。
    public class CompProperties_AbilityEffect_ForceJob : CompProperties_AbilityEffect
    {
        public float radius = 6f;
        public JobDef jobDef;

        public int durationTick;

        public CompProperties_AbilityEffect_ForceJob()
        {
            compClass = typeof(CompAbilityEffect_ForceJob);
        }
    }

    // 对施法者周围的敌人强行下达指定 Job，用于嘲讽等控制类能力。
    public class CompAbilityEffect_ForceJob : CompAbilityEffect
    {
        private new CompProperties_AbilityEffect_ForceJob Props
        {
            get { return props as CompProperties_AbilityEffect_ForceJob; }
        }

        // 执行能力时的效果
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent?.pawn;
            Map map = caster?.Map;
            CompProperties_AbilityEffect_ForceJob forceProps = Props;
            if (map?.mapPawns == null || forceProps?.jobDef == null || forceProps.radius < 0f)
            {
                return;
            }

            float radiusSquared = forceProps.radius * forceProps.radius;

            // 以施法者为中心扫描敌人，半径判断使用平方距离避免重复开方。
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (!CanForceJobOn(p, caster, map, radiusSquared))
                {
                    continue;
                }

                StartForcedJob(p, caster, forceProps);
            }

            base.Apply(target, dest);
        }

        private bool CanForceJobOn(Pawn target, Pawn caster, Map map, float radiusSquared)
        {
            return target != null
                && target != caster
                && target.Spawned
                && target.Map == map
                && target.jobs != null
                && !target.Dead
                && !target.Downed
                && target.HostileTo(caster)
                && target.Position.DistanceToSquared(caster.Position) <= radiusSquared;
        }

        private void StartForcedJob(Pawn target, Pawn caster, CompProperties_AbilityEffect_ForceJob forceProps)
        {
            if (forceProps?.jobDef == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(forceProps.jobDef, caster);
            if (forceProps.durationTick > 0)
            {
                // XML 中的 durationTick 表示强制任务最多维持多久；不设置时沿用 JobDef 自身结束条件。
                job.expiryInterval = forceProps.durationTick;
                job.checkOverrideOnExpire = true;
            }

            target.jobs.StopAll(false, true);
            target.jobs.StartJob(job, JobCondition.InterruptForced);

            // 强制任务之外还要指定敌人目标，否则部分 AI 会在下一次决策时转火。
            if (target.mindState != null)
            {
                target.mindState.enemyTarget = caster;
            }
        }

        // AI 只在目标正瞄准自己时使用，避免该能力变成无条件群控。
        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn caster = parent?.pawn;
            if (caster == null || MugirlWildSlaveUtility.IsPlayerFaction(caster.Faction))
            {
                return false;
            }
            if (target.HasThing)
            {
                Pawn targetPawn = target.Thing as Pawn;
                if (targetPawn != null && !targetPawn.Dead && !targetPawn.Downed)
                {
                    return targetPawn.TargetCurrentlyAimingAt == caster;
                }
            }
            return false;
        }
    }
}
