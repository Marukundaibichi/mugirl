using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 抽象基类：定义采集动物身体资源的工作驱动逻辑
    public abstract class JobDriver_GatherBodyResources : JobDriver
    {
        private float gatherProgress; // 采集进度
        private Pawn activeGatherTarget;
        private bool gatherEffectsActive;

        // 目标索引常量：动物目标
        protected const TargetIndex AnimalInd = TargetIndex.A;

        // 抽象属性：获取总工作量（由子类实现）
        protected abstract float WorkTotal { get; }

        // 抽象方法：获取动物的身体资源组件（由子类实现）
        protected abstract CompMooHasBodyResource GetComp(Pawn animal);

        // 数据暴露方法，用于存档/读档
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<float>(ref this.gatherProgress, "gatherProgress", 0f, false);
        }

        // 尝试进行前置预订（确保目标可用）
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn actor = this.pawn;
            if (!CanDoGatherWork(actor))
            {
                return false;
            }

            LocalTargetInfo target = this.job.GetTarget(TargetIndex.A);
            Job job = this.job;
            return ReservationUtility.Reserve(actor, target, job, 1, 1, null, errorOnFailed);
        }

        // 创建新的工作步骤（Toils）
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 设置失败条件：目标消失/禁用/不可交互
            ToilFailConditions.FailOnDespawnedNullOrForbidden(this, TargetIndex.A);
            this.FailOn(() => !CanDoGatherWork(pawn));

            Pawn targetPawn = (Pawn)this.job.GetTarget(TargetIndex.A).Thing;
            this.AddFinishAction(delegate (JobCondition condition)
            {
                CleanupGatherEffects(targetPawn);
            });

            // 情况1：目标是自己（自采集）
            if (targetPawn == this.pawn)
            {
                // 移动到目标位置
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

                // 创建等待/工作步骤
                Toil wait = new Toil();
                wait.initAction = delegate ()
                {
                    BeginGatherEffects(wait.actor, targetPawn);
                };

                wait.tickAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    if (!CanDoGatherWork(actor))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    TickGatherEffects(actor, targetPawn);
                    actor.skills.Learn(SkillDefOf.Animals, 0.13f, false);  // 增加动物技能经验
                    CompMooHasBodyResource comp = GetComp(targetPawn);
                    gatherProgress += GatherProgressPerTick(actor, targetPawn, comp);  // 根据设置统计采集进度

                    // 完成采集
                    if (gatherProgress >= WorkTotal)
                    {
                        CompleteGather(this.pawn);  // 调用采集完成逻辑
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);  // 结束当前工作
                    }
                };

                // 工作结束时的清理逻辑
                wait.AddFinishAction(delegate ()
                {
                    CleanupGatherEffects(targetPawn);
                });

                // 设置失败条件
                ToilFailConditions.FailOnDespawnedOrNull<Toil>(wait, TargetIndex.A);
                ToilFailConditions.FailOnCannotTouch<Toil>(wait, TargetIndex.A, PathEndMode.Touch);

                // 动态判断工作是否可完成
                wait.AddEndCondition(delegate ()
                {
                    CompMooHasBodyResource comp = GetComp((Pawn)((Thing)this.job.GetTarget(TargetIndex.A)));
                    if (!CanGather(comp))
                    {
                        return JobCondition.Incompletable;  // 资源不可用时标记为不可完成
                    }
                    return JobCondition.Ongoing;
                });

                wait.defaultCompleteMode = ToilCompleteMode.Never; // 手动控制完成
                ToilEffects.WithProgressBar(wait, TargetIndex.A, () => this.gatherProgress / this.WorkTotal, false, -0.5f); // 显示进度条
                wait.activeSkill = (() => SkillDefOf.Animals);  // 设置关联技能
                yield return wait;
            }
            // 情况2：目标是其他动物
            else
            {
                // 移动到目标位置
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

                // 创建等待/工作步骤
                Toil wait = new Toil();
                wait.initAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    actor.pather.StopDead();  // 立即停止移动
                    PawnUtility.ForceWait(targetPawn, 15000, actor, true);  // 强制目标等待并面向榨乳者
                    BeginGatherEffects(actor, targetPawn);
                };

                wait.tickAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    if (!CanDoGatherWork(actor))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    TickGatherEffects(actor, targetPawn);
                    actor.skills.Learn(SkillDefOf.Animals, 0.13f, false);  // 增加动物技能经验
                    CompMooHasBodyResource comp = GetComp(targetPawn);
                    gatherProgress += GatherProgressPerTick(actor, targetPawn, comp);  // 统计采集进度

                    // 完成采集
                    if (gatherProgress >= WorkTotal)
                    {
                        CompleteGather(this.pawn);  // 调用采集完成逻辑
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);  // 结束当前工作
                    }
                };

                // 工作结束时的清理逻辑（同自采集情况）
                wait.AddFinishAction(delegate ()
                {
                    CleanupGatherEffects(targetPawn);
                });

                // 设置失败条件（同自采集情况）
                ToilFailConditions.FailOnDespawnedOrNull<Toil>(wait, TargetIndex.A);
                ToilFailConditions.FailOnCannotTouch<Toil>(wait, TargetIndex.A, PathEndMode.Touch);
                wait.AddEndCondition(delegate ()
                {
                    CompMooHasBodyResource comp = GetComp((Pawn)((Thing)this.job.GetTarget(TargetIndex.A)));
                    if (!CanGather(comp))
                    {
                        return JobCondition.Incompletable;
                    }
                    return JobCondition.Ongoing;
                });

                wait.defaultCompleteMode = ToilCompleteMode.Never;
                ToilEffects.WithProgressBar(wait, TargetIndex.A, () => this.gatherProgress / this.WorkTotal, false, -0.5f);
                wait.activeSkill = (() => SkillDefOf.Animals);
                yield return wait;
            }

            yield break; // 结束枚举
        }

        // 虚方法：子类可重写以改变采集行为（如使用固定产量而非按比例）
        protected virtual void CompleteGather(Pawn doer)
        {
            GetComp((Pawn)((Thing)job.GetTarget(TargetIndex.A))).Gathered(doer);
        }

        protected virtual void StartGatherEffects(Pawn doer, Pawn target)
        {
        }

        protected virtual void TickGatherEffects(Pawn doer, Pawn target)
        {
        }

        protected virtual void EndGatherEffects(Pawn doer, Pawn target)
        {
        }

        private void BeginGatherEffects(Pawn doer, Pawn target)
        {
            activeGatherTarget = target;
            gatherEffectsActive = true;
            StartGatherEffects(doer, target);
        }

        private void CleanupGatherEffects(Pawn target)
        {
            Pawn cleanupTarget = target ?? activeGatherTarget;
            bool wasActive = gatherEffectsActive;
            if (gatherEffectsActive)
            {
                EndGatherEffects(pawn, cleanupTarget);
                gatherEffectsActive = false;
                activeGatherTarget = null;
            }

            if (wasActive && cleanupTarget != null && cleanupTarget != pawn && cleanupTarget.CurJobDef == JobDefOf.Wait_MaintainPosture)
            {
                cleanupTarget.jobs.EndCurrentJob(JobCondition.InterruptForced, true);  // 强制中断目标的等待工作
            }
        }

        protected virtual float GatherProgressPerTick(Pawn actor, Pawn target, CompMooHasBodyResource comp)
        {
            return 1f;
        }

        private bool CanGather(CompMooHasBodyResource comp)
        {
            if (comp == null || !comp.Active)
            {
                return false;
            }

            if (job.playerForced)
            {
                return comp.Fullness > 0f;
            }

            return comp.Fullness >= comp.MilkThreshold;
        }

        protected virtual bool CanDoGatherWork(Pawn gatherer)
        {
            return gatherer?.RaceProps?.Humanlike == true && !gatherer.RaceProps.IsMechanoid && gatherer.skills != null;
        }
    }
}
