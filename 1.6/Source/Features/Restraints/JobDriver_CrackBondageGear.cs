using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_CrackBondageGear : JobDriver
    {
        protected TargetIndex iitem = TargetIndex.A;  // 破解工具
        protected TargetIndex appear = TargetIndex.B; // 目标装备

        // 获取物品和装备
        protected Thing item
        {
            get
            {
                LocalTargetInfo target = base.job.GetTarget(iitem);
                if (!target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected Apparel targetApparel
        {
            get
            {
                LocalTargetInfo target = base.job.GetTarget(appear);
                if (!target.HasThing)
                    return null;
                return target.Thing as Apparel;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing itemToUse = item;
            Apparel apparelToCrack = targetApparel;
            if (itemToUse == null || apparelToCrack == null)
            {
                return false;
            }

            bool itemAlreadyHeld = pawn.inventory != null && pawn.inventory.Contains(itemToUse);
            bool reserveItem = itemAlreadyHeld || pawn.Reserve(itemToUse, job, 1, -1, null, errorOnFailed);
            bool reserveApparel = pawn.Reserve(apparelToCrack, job, 1, -1, null, errorOnFailed);

            return reserveItem && reserveApparel;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Thing itemToUse = item;
            Apparel apparel = targetApparel;
            if (itemToUse == null || apparel == null || !apparel.Spawned || !apparel.IsAdvancedApparel() || apparel.IsUnlockAdvancedApparel())
            {
                yield break;
            }

            yield return Toils_Reserve.Reserve(appear);

            if (pawn.inventory != null && pawn.inventory.Contains(itemToUse))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, iitem);
            }
            else if (itemToUse.Spawned)
            {
                yield return Toils_Reserve.Reserve(iitem);
                yield return Toils_Goto.GotoThing(iitem, PathEndMode.ClosestTouch).FailOnForbidden(iitem);
                yield return new Toil
                {
                    initAction = () =>
                    {
                        if (itemToUse == null || itemToUse.Destroyed || pawn.carryTracker.TryStartCarry(itemToUse, 1) <= 0)
                        {
                            pawn.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                        }
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }
            else
            {
                yield break;
            }

            yield return Toils_Goto.GotoThing(appear, PathEndMode.Touch);

            yield return Toils_General.Wait(60)
                .WithProgressBarToilDelay(appear)
                .FailOnDespawnedNullOrForbidden(appear);

            yield return new Toil
            {
                initAction = () =>
                {
                    if (apparel.Destroyed || !apparel.Spawned || !apparel.IsAdvancedApparel() || apparel.IsUnlockAdvancedApparel())
                    {
                        return;
                    }

                    CompTargetCrackBondageGear compEffect = itemToUse.TryGetComp<CompTargetCrackBondageGear>();
                    compEffect?.DoEffectOn(pawn, apparel);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
