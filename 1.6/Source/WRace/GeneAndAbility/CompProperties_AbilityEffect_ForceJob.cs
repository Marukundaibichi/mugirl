using RimWorld;
using System.Collections.Generic;
using System;
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
            compClass = typeof(CompAbilityEffect_ForceJob); // 指定处理类
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
            Map map = caster.Map;

            // 以自己为中心 AOE
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == caster) continue;
                if (p.Dead || p.Downed) continue;
                if (!p.HostileTo(caster)) continue;

                if (p.Position.DistanceTo(caster.Position) > Props.radius)
                    continue;

                Job job = JobMaker.MakeJob(Props.jobDef, caster);

                p.jobs.StopAll(false, true);
                p.jobs.StartJob(job, JobCondition.InterruptForced);

                // 明确拉仇恨（这是“嘲讽”的关键）
                p.mindState.enemyTarget = caster;
            }

            base.Apply(target, dest);
        }


        // 强制目标执行任务
        private void ForceJob(Pawn pawn)
        {
            if (pawn == null) return;

            // 创建任务
            Job job = JobMaker.MakeJob(Props.jobDef, parent.pawn);

            job.expiryInterval = Props.durationTick; // 设置任务的持续时间

            // 停止当前任务并开始新的任务
            pawn.jobs.StopAll(false, true);
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);

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
