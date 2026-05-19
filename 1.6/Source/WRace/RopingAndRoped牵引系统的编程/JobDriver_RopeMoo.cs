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
                    bool isColonist = pawn.IsColonistPlayerControlled;
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

                    actor.roping.RopePawn(pawn);

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
            // 1. 计算接受牵引概率（这里直接用 Arrest 公式，你也可以自定义）
            float acceptChance = ropee.GetAcceptArrestChance(roper); // 可复用原版公式

            // 2. 派系通知（可选）
            Faction homeFaction = ropee.HomeFaction;
            if (homeFaction != null && homeFaction != roper.Faction)
            {
                // 如果你想加派系反应，可以在这里处理
                homeFaction.Notify_MemberCaptured(ropee, roper.Faction);
            }

            List<ThingComp> comps = ropee.AllComps;

            // 3. 接受牵引条件：倒地、不能打架 或 概率成功
            if (ropee.WorkTagIsDisabled(WorkTags.Violent) || Rand.Value < acceptChance)
            {
                foreach (var comp in comps)
                {
                    comp.Notify_Arrested(true); // 暂时沿用逮捕的通知
                }
                return true; // 接受牵引
            }

            // 4. 拒绝牵引
            Messages.Message("MessageRefusedRope".Translate(ropee.LabelShort, ropee), ropee, MessageTypeDefOf.ThreatSmall, true);
            foreach (var comp in comps)
            {
                comp.Notify_Arrested(false); // 暂时沿用逮捕的通知
            }

            // 5. 如果不是敌人 → 进入狂暴并攻击牵引者
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
