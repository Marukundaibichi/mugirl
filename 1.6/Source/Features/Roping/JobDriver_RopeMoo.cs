using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_RopeMoo : JobDriver
    {
        private Pawn Target => (Pawn)this.job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return this.pawn.Reserve(this.Target, this.job, 1, -1, null, errorOnFailed, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null, false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch, false)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_RopeMoo.RopePawn(TargetIndex.A);
        }
        private const TargetIndex PawnInd = TargetIndex.A;

    }
    public static class Toils_RopeMoo
    {
        public static Toil RopePawn(TargetIndex ropeeInd)
        {
            Toil toil = ToilMaker.MakeToil("RopePawn");

            toil.initAction = delegate ()
            {
                Pawn actor = toil.actor;
                Pawn pawn = actor.jobs.curJob.GetTarget(ropeeInd).Thing as Pawn;

                if (pawn != null)
                {
                    bool isSlaveOrPrisoner = pawn.IsSlave || pawn.IsPrisonerOfColony;

                    // 只有当目标是殖民者且非奴隶非囚犯时才进行拒绝牵引判断
                    if (!isSlaveOrPrisoner)
                    {
                        if (!CheckAcceptRope(pawn, actor))
                        {
                            actor.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
                            return;
                        }
                    }

                    // 牵引逻辑：初始化 RopeTracker 并执行牵引
                    if (pawn.roping == null)
                    {
                        pawn.roping = new Pawn_RopeTracker(pawn);
                    }

                    if (actor.roping == null)
                    {
                        actor.roping = new Pawn_RopeTracker(actor);
                    }

                    actor.roping.RopePawn(pawn);
                    RopingService.RegisterPawnRope(actor, pawn);

                    Pawn_CallTracker caller = pawn.caller;
                    if (caller != null)
                    {
                        caller.DoCall();
                    }

                    PawnUtility.ForceWait(pawn, 30, actor, false, false);
                }
            };

            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.defaultDuration = 30;
            toil.FailOnDespawnedOrNull(ropeeInd);
            toil.PlaySustainerOrSound(() => SoundDefOf.Roping, 1f);
            return toil;
        }

        private static bool CheckAcceptRope(Pawn ropee, Pawn roper)
        {
            if (RopingService.IsMooGirlRopee(ropee))
            {
                foreach (var comp in ropee.AllComps)
                {
                    comp.Notify_Arrested(true);
                }
                return true;
            }

            // 接受概率沿用原版逮捕公式，避免改变既有数值。
            float acceptChance = ropee.GetAcceptArrestChance(roper);

            Faction homeFaction = ropee.HomeFaction;
            if (homeFaction != null && homeFaction != roper.Faction)
            {
                homeFaction.Notify_MemberCaptured(ropee, roper.Faction);
            }

            List<ThingComp> comps = ropee.AllComps;

            // 接受条件保持旧逻辑：不能暴力或概率成功。
            if (ropee.WorkTagIsDisabled(WorkTags.Violent) || Rand.Value < acceptChance)
            {
                foreach (var comp in comps)
                {
                    comp.Notify_Arrested(true);
                }
                return true;
            }

            Messages.Message("MessageRefusedRope".Translate(ropee.LabelShort, ropee), ropee, MessageTypeDefOf.ThreatSmall, true);
            foreach (var comp in comps)
            {
                comp.Notify_Arrested(false);
            }

            // 拒绝牵引后的狂暴逻辑保持旧行为。
            if (ropee.Faction == null || !roper.HostileTo(ropee))
            {
                bool startedBerserk = ropee.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.Berserk,
                    "RopeRejected".Translate(),
                    false, false, false, null, false, false, false
                );
                if (startedBerserk)
                {
                    Job fightJob = JobMaker.MakeJob(JobDefOf.AttackMelee, roper);
                    ropee.jobs.StartJob(fightJob);
                }
            }

            return false;
        }

    }
}
