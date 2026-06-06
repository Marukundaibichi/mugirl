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
            get { return (CompProperties_AbilityEffect_ForceJob)this.props; }
        }

        // 执行能力时的效果
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null || Props.jobDef == null)
            {
                return;
            }

            // 以自己为中心 AOE
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == caster) continue;
                if (p.Dead || p.Downed) continue;
                if (!p.HostileTo(caster)) continue;

                if (p.Position.DistanceTo(caster.Position) > Props.radius)
                {
                    continue;
                }

                Job job = JobMaker.MakeJob(Props.jobDef, caster);

                p.jobs.StopAll(false, true);
                p.jobs.StartJob(job, JobCondition.InterruptForced);

                // 强制任务之外还要指定敌人目标，否则部分 AI 会在下一次决策时转火。
                p.mindState.enemyTarget = caster;
            }

            base.Apply(target, dest);
        }
        // 判断目标是否可以被AI选择
        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            if (this.parent.pawn.Faction == Faction.OfPlayer)
            {
                return false;
            }
            if (target.HasThing)
            {
                Pawn targetPawn = target.Thing as Pawn;
                if (targetPawn != null)
                {
                    return targetPawn.TargetCurrentlyAimingAt == this.parent.pawn;
                }
            }
            return false;
        }
    }
}
