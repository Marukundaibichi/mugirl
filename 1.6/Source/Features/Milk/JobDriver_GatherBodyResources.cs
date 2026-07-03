using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 身体资源采集 JobDriver 基类，负责预约、等待、进度条和采集结束清理。
    public abstract class JobDriver_GatherBodyResources : JobDriver
    {
        private float gatherProgress;
        private Pawn activeGatherTarget;
        private bool gatherEffectsActive;

        protected const TargetIndex AnimalInd = TargetIndex.A;

        protected Pawn TargetPawn => job.GetTarget(AnimalInd).Thing as Pawn;

        private int forcedWaitJobLoadId = -1;

        protected abstract float WorkTotal { get; }

        protected abstract CompMooHasBodyResource GetComp(Pawn animal);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<float>(ref this.gatherProgress, "gatherProgress", 0f, false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn actor = this.pawn;
            Pawn targetPawn = TargetPawn;
            if (!CanDoGatherWork(actor) || targetPawn == null)
            {
                return false;
            }

            if (!CanGather(GetComp(targetPawn)))
            {
                return false;
            }

            Job job = this.job;
            return ReservationUtility.Reserve(actor, targetPawn, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            ToilFailConditions.FailOnDespawnedNullOrForbidden(this, TargetIndex.A);
            this.FailOn(() => TargetPawn == null || !CanDoGatherWork(pawn));

            Pawn targetPawn = TargetPawn;
            if (targetPawn == null)
            {
                yield break;
            }

            this.AddFinishAction(delegate (JobCondition condition)
            {
                CleanupGatherEffects();
            });

            // 自采集不需要控制目标 Pawn，只推进自己的工作进度和动画效果。
            if (targetPawn == this.pawn)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

                Toil wait = new Toil();
                wait.initAction = delegate ()
                {
                    BeginGatherEffects(wait.actor, targetPawn);
                };

                wait.tickAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    CompMooHasBodyResource comp = GetComp(targetPawn);
                    if (!CanDoGatherWork(actor) || !CanGather(comp))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    TickGatherEffects(actor, targetPawn);
                    actor.skills.Learn(SkillDefOf.Animals, 0.13f, false);
                    gatherProgress += GatherProgressPerTick(actor, targetPawn, comp);

                    if (gatherProgress >= WorkTotal)
                    {
                        CompleteGather(this.pawn);
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);
                    }
                };

                wait.AddFinishAction(delegate ()
                {
                    CleanupGatherEffects();
                });

                ToilFailConditions.FailOnDespawnedOrNull<Toil>(wait, TargetIndex.A);
                ToilFailConditions.FailOnCannotTouch<Toil>(wait, TargetIndex.A, PathEndMode.Touch);

                wait.AddEndCondition(delegate ()
                {
                    CompMooHasBodyResource comp = GetComp(targetPawn);
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
            else
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

                // 他采集会让目标保持等待姿态；结束时只中断这里创建的等待 job。
                Toil wait = new Toil();
                wait.initAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    actor.pather.StopDead();
                    Job targetWaitJob = JobMaker.MakeJob(Mugirl_DefOf.Job_MugirlMilkingTargetWait, actor);
                    targetWaitJob.expiryInterval = 15000;
                    targetPawn.jobs.StartJob(targetWaitJob, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
                    forcedWaitJobLoadId = targetPawn.CurJob != null ? targetPawn.CurJob.loadID : -1;
                    BeginGatherEffects(actor, targetPawn);
                };

                wait.tickAction = delegate ()
                {
                    Pawn actor = wait.actor;
                    CompMooHasBodyResource comp = GetComp(targetPawn);
                    if (!CanDoGatherWork(actor) || !CanGather(comp))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    TickGatherEffects(actor, targetPawn);
                    actor.skills.Learn(SkillDefOf.Animals, 0.13f, false);
                    gatherProgress += GatherProgressPerTick(actor, targetPawn, comp);

                    if (gatherProgress >= WorkTotal)
                    {
                        CompleteGather(this.pawn);
                        actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);
                    }
                };

                wait.AddFinishAction(delegate ()
                {
                    CleanupGatherEffects();
                });

                ToilFailConditions.FailOnDespawnedOrNull<Toil>(wait, TargetIndex.A);
                ToilFailConditions.FailOnCannotTouch<Toil>(wait, TargetIndex.A, PathEndMode.Touch);
                wait.AddEndCondition(delegate ()
                {
                    CompMooHasBodyResource comp = GetComp(targetPawn);
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

            yield break;
        }

        // 子类可重写采集完成行为，例如改成固定产量或附加特效。
        protected virtual void CompleteGather(Pawn doer)
        {
            CompMooHasBodyResource comp = GetComp(TargetPawn);
            if (CanGather(comp))
            {
                comp.Gathered(doer);
            }
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

        private void CleanupGatherEffects()
        {
            Pawn cleanupTarget = activeGatherTarget;
            bool wasActive = gatherEffectsActive;
            if (gatherEffectsActive)
            {
                EndGatherEffects(pawn, cleanupTarget);
                gatherEffectsActive = false;
                activeGatherTarget = null;
            }

            if (wasActive && ShouldEndForcedWait(cleanupTarget))
            {
                cleanupTarget.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }

            forcedWaitJobLoadId = -1;
        }

        protected virtual float GatherProgressPerTick(Pawn actor, Pawn target, CompMooHasBodyResource comp)
        {
            return 1f;
        }

        private bool ShouldEndForcedWait(Pawn cleanupTarget)
        {
            if (cleanupTarget == null || cleanupTarget.Destroyed || cleanupTarget == pawn || cleanupTarget.CurJobDef != Mugirl_DefOf.Job_MugirlMilkingTargetWait)
            {
                return false;
            }

            Job currentJob = cleanupTarget.CurJob;
            return currentJob != null && currentJob.loadID == forcedWaitJobLoadId;
        }

        protected virtual bool CanGather(CompMooHasBodyResource comp)
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
