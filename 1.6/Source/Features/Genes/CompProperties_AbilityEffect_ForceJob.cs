using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 用于定义强制任务的能力效果
    public class CompProperties_AbilityEffect_ForceJob : CompProperties_AbilityEffect
    {
        public float radius = 6f;
        // 定义目标任务
        public JobDef jobDef;

        // 持续时间乘数
        public int durationTick;

        public CompProperties_AbilityEffect_ForceJob()
        {
            compClass = typeof(CompAbilityEffect_ForceJob);
        }
    }

    // 用于执行强制任务的能力效果
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

            // 以自己为中心 AOE
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

        // 判断目标是否可以被AI选择
        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn caster = parent?.pawn;
            if (caster == null || MooGirlWildSlaveUtility.IsPlayerFaction(caster.Faction))
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
